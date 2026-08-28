# 架构设计草案

> 本文描述 MVP 的模块与接口方向。具体类型会在第一个可执行纵向切片中验证。

## 1. 技术栈

- Windows 11 优先。
- C# 14 与 .NET 10 LTS。
- WPF 主界面，按需使用 Windows App SDK 与 Win32 能力。
- SQLite 保存规则、索引元数据和操作历史。
- 当前采用未签名的自包含单文件 ZIP 与当前用户 PowerShell 安装器，仅供本机使用；发行门禁验证 SHA256、结构和安装生命周期，不读取证书存储。

选择 WPF 的原因不是界面更先进，而是此产品高度依赖文件系统、托盘、快捷键和 Windows Shell，成熟度与可调试性比跨平台更重要。界面与系统集成应通过模块接口隔离，未来迁移 UI 不应重写整理规则和操作日志。

## 2. 架构原则

1. 文件系统是桌面项目状态的事实来源。
2. SQLite 是索引和历史，不是用户文件的事实来源。
3. 规则计算是纯逻辑，不产生文件副作用。
4. 整理计划不可执行自身；执行必须经过独立接口。
5. 文件操作需要持久化日志支持故障恢复。
6. UI 只调用模块接口，不直接移动文件。
7. 只有存在生产与测试两种实现时，才建立真实适配器接缝。

## 3. 推荐模块

### DesktopCatalog

**职责**：解析桌面位置、扫描项目、合并文件变化并提供当前快照。

**接口**：

```text
GetSnapshot() -> DesktopSnapshot
ObserveChanges(callback) -> IDisposable observation
Refresh() -> DesktopSnapshot
```

该模块隐藏 OneDrive 重定向、文件系统监听、变化事件和收件箱过滤等实现细节。Hidden/System 属性、未完成下载后缀以及 Office `~$` 临时文件在扫描和可判断的监听事件中被排除。`CombinedDesktopCatalog` 合并主监控目录和公共桌面只读目录；连续变化先进行 300ms 合并，再按路径增量刷新，监听缓冲区异常事件触发完整快照复原。

### OrganizationPlanner

**职责**：根据桌面项目和规则生成不可变整理计划，解析目标路径并识别风险，不接触文件系统。首个纵向切片将原计划中的 RuleEngine 与 PlanBuilder 收敛到同一个深模块，避免 UI 学习两个浅接口。

**接口**：

```text
CreatePlan(items, rules, managedDirectory) -> OrganizationPlan
Validate(plan, currentSnapshot) -> PlanValidation
ExcludeItems(draftPlan, itemIds) -> OrganizationPlan
KeepOnlyItems(draftPlan, itemIds) -> OrganizationPlan
AdjustTarget(draftPlan, itemId, relativeDestination, managedDirectory) -> OrganizationPlan
```

计划项保存生成时观察到的文件大小与修改时间；执行前按规范化源路径对照最新快照，源项目缺失或变化时拒绝执行。计划编辑方法返回新的不可变草稿，不接触文件系统或规则存储；目标调整将相对目录规范化到托管根下并保留源文件名。`KeepOnlyItems` 在仍有规则冲突时拒绝操作，避免 UI 用选择范围绕过冲突裁决。

### ExecutionGate

**职责**：把 Draft 整理计划和最新桌面快照复核为执行审查并生成风险摘要；只有计划非空、无冲突且快照有效时才返回可执行计划。

**接口**：

```text
Review(draftPlan, currentSnapshot) -> ExecutionReview
PrepareForExecution(review) -> confirmedPlan
```

用户点击立即执行后，应用读取最新快照并进行 Review，再调用文件系统预检；任一环节发现源项目变化或阻断项都会停止，全部通过则直接执行。演示和真实桌面共用该流程。

### OrganizationPlanAnalyzer

**职责**：从不可变整理计划派生统一的计划摘要，不读取文件系统。摘要包含执行、排除与冲突数量、已知总大小、未知文件夹数量、目标目录分布和计划内目标同名数。

`PlanItem` 保存生成计划时观察到的项目种类，因此分析器可把文件夹大小明确视为未知，而无需递归扫描目录。主界面和 `ExecutionGate` 复用同一分析器，避免预览统计与执行口径漂移。

### FileOrganizer

**职责**：执行已确认的整理计划、处理冲突，并返回逐项结果。

`Inspect(plan)` 是文件系统安全检查的统一接口，返回阻断项、警告项和可用空间；WPF 的立即执行入口与 `ExecuteAsync` 内部不变量共同使用它。文件夹跨卷移动先复制到带操作 ID 的托管暂存路径，完成后在目标卷内改名，最后删除源目录；暂存清理拒绝遍历重解析点。

撤销使用新的 `OperationKind.Undo` 操作记录和 `ReversesOperationId` 引用，不更新原整理记录。SQLite 初始化会为旧数据库增量添加两列；备份恢复根据操作方向交换路径白名单，并要求撤销记录引用同范围内的安全原操作。

**接口**：

```text
Execute(confirmedPlan) -> OrganizationOperation
Undo(operationId) -> UndoOperation
RecoverInterrupted(limit) -> OrganizationOperation[]
```

该模块的接口刻意很小。目标重名时使用 `名称 (1).扩展名` 安全命名，先持久化实际目标再移动，不覆盖已有文件。占用、部分成功、恢复等行为继续隐藏在实现中。构造时必须提供允许的源根目录和目标根目录，两者相同或互相包含时立即拒绝；执行、撤销与恢复都会进行路径边界校验。恢复仅核对 `Running` 操作：源消失且目标存在时补记成功，其余不明确状态记为失败，绝不自动续跑文件移动。

### OperationJournal

**职责**：持久化计划、操作项、结果和撤销关系，支持异常退出恢复。

**接口**：

```text
Save(operation) -> void
Get(operationId) -> OrganizationOperation
List(limit) -> recent OrganizationOperation[]
```

日志写入顺序保证：开始记录先于文件变更，安全重命名后的真实目标也先于文件移动落盘，项目结果紧随每次变更。`IOperationJournal` 是真实接缝：WPF 应用使用 SQLite，JSON 适配器用于兼容和测试。SQLite 使用操作批次表与有序操作项表，每次保存处于同一事务；List 按开始时间倒序返回完整操作。应用启动时先对账最近 50 条中的中断操作，再加载历史和最近可撤销操作；历史页面可查看逐项源路径、目标路径、状态与失败原因。

演示日志保存在系统临时目录，真实桌面日志独立保存在 `%LOCALAPPDATA%\DesktopManager\real-operations.db`。应用分别用演示根目录和真实桌面/授权托管目录构造 `FileOrganizer` 进行启动对账，禁止日志跨作用域进入错误的路径白名单。

### OperationHistory

**职责**：合并多个 `IOperationJournal` 的最近操作，统一排序和截取，同时保留每条操作的演示或真实桌面作用域。

```text
List(limit) -> ScopedOrganizationOperation[]
```

历史页面通过该模块同时展示两套 SQLite 数据，并增加范围列。合并模块不执行撤销，也不暴露数据库路径或存储结构；撤销仍需根据作用域选择正确日志和路径白名单。

### WindowsShellAdapter

**职责**：封装桌面位置解析、托盘、全局快捷键、开机启动和 Shell 通知。

它是 Windows 专属实现。测试使用受控目录和内存事件源，避免测试真实桌面。

### ApplicationUI

**职责**：呈现收件箱、整理计划、规则、历史和设置，并编排模块调用。

UI 不拥有规则决策和文件操作逻辑。真实桌面模式读取已保存配置后调用同一个 `OrganizationPlanner` 生成 `Draft` 计划。用户点击立即执行后，UI 用最新快照调用 `ExecutionGate`，再由 `FileOrganizer.ExecuteAsync` 异步完成预检并以真实日志、真实桌面源根与托管根执行。目录递归预检、跨卷文件夹复制及物理移动均不得占用 WPF Dispatcher 线程，执行期间按钮进入不可重入的“正在收纳”状态，桌面变化通知也不得覆盖该状态。

预览区对当前草稿的排除、仅保留选中项和目标调整都委托给 `OrganizationPlanner`。UI 只收集选择与相对目录，不直接拼装新计划；重新扫描、修改规则或项目处置偏好仍会整体作废草稿。

真实撤销从历史记录的 `OperationScope` 选择日志与路径白名单，不能跨作用域。撤销无需会话授权或额外确认；路径与目标冲突仍由 `FileOrganizer` 以不覆盖策略处理。

### SingleInstanceCoordinator

**职责**：保证当前 Windows 登录会话内只运行一个桌面管理实例，并把后续启动转换为已有主窗口的激活请求。

```text
TryAcquire(activationRequested) -> bool
```

模块使用当前用户会话范围内的命名互斥量和自动重置激活事件。首个实例监听事件；后续实例发送信号后退出。WPF 应用收到信号后通过 Dispatcher 恢复、显示并激活主窗口。内核对象随进程释放，不保存实例锁文件。

### WindowLifetimeController 与 TrayIconController

`WindowLifetimeController` 决定普通关闭应隐藏到托盘，还是在用户明确请求后退出应用。状态只允许从后台运行转换为退出，关闭事件不会自行结束进程。

`TrayIconController` 是 Windows 托盘适配器，负责图标、待整理数量、双击恢复、“打开收件箱”、“显示所有收纳窗口”、“隐藏所有收纳窗口”和“退出应用”菜单。WPF 生命周期负责把托盘动作编排到窗口显示与退出状态；系统会话结束时先请求退出，避免注销或关机被隐藏逻辑阻止。托盘首次隐藏提示每个进程最多显示一次，减少打扰。

`GlobalHotKeyBinding` 定义用户可见组合键、受支持按键白名单及 Windows 原生键值之间的稳定契约。`GlobalHotKeyController` 在 WPF 窗口句柄初始化后注册配置组合，监听 `WM_HOTKEY` 并复用现有的收件箱恢复流程。运行时替换会先释放旧注册；新组合被占用时重新注册旧组合，启动时配置组合被占用则尝试回退 `Ctrl + Alt + Space`。只有注册成功的组合才写入设置，应用退出时显式注销热键。

`IStartupRegistration` 是开机启动的应用边界，`WindowsStartupRegistration` 负责生成带引号的可执行文件命令及启停语义，`CurrentUserRunStartupValueStore` 只负责读写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 中的单个应用值。开机启动默认关闭且只能由用户在设置页主动修改；启动命令使用 `--background`，应用完成初始化后隐藏到托盘。

`NotificationPolicy` 根据通知总开关、本地当前时间和免打扰区间返回显示、全局抑制或静音抑制决定，同时支持同日和跨午夜区间。WPF 页面只发布桌面变化、整理完成和撤销完成事件；应用生命周期仅在主窗口隐藏且策略允许时调用托盘适配器。通知设置保存在 JSON，默认开启并在 `22:00–08:00` 免打扰；故障警告不经过普通状态通知策略。

### 发行生命周期

发行脚本按照 [.NET 官方单文件部署模型](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)生成指定 Windows RID 的自包含单文件程序，并将原生库设置为随包提取，最终输出 ZIP 与 SHA256。安装器只写当前用户目录和 HKCU，使用同卷暂存目录与备份目录完成原位替换；升级失败时恢复旧安装。设置和 SQLite 操作历史位于独立的 `%LOCALAPPDATA%\DesktopManager`，升级与默认卸载不会修改它们。

卸载器拒绝删除正在运行的应用，清理当前用户开机启动项、开始菜单快捷方式和卸载登记。递归移动与删除前会验证绝对目标、拒绝根目录和重解析点；只有显式 `-RemoveUserData` 才删除用户设置。`Uninstall.cmd` 异步启动已加载的 PowerShell 卸载器，使批处理先退出，避免安装目录删除自身后 `cmd.exe` 再次读取批处理失败。自动验收在临时目录并关闭 Shell 集成，覆盖安装、升级、默认保留设置、卸载、ZIP 结构和哈希。Windows Sandbox 额外使用只读工作区与独立可写结果目录，临时信任不含私钥的公开证书后强制验签，并通过真实 `Install.cmd` 和 `Uninstall.cmd` 验证安装、WPF 启动、正常退出、异步卸载完成及无安装目录残留。

设置页通过 Windows 目录选择器收集监控目录和托管目录，并复用 `FileOrganizer` 的根目录不变量进行校验。业务设置采用单一 JSON 存储，保存位置为本机 `%LOCALAPPDATA%\DesktopManager\settings.json`；旧版本的授权字段会被反序列化器忽略。开机启动以 Windows 当前用户启动项为唯一事实来源，避免双重状态。

同一 JSON 文件还保存 `OrganizationRule` 规则集合。单条规则可组合扩展名、文件名关键词、闭区间大小、最近修改天数和项目类型；不同条件种类采用 AND，同类候选采用 OR，空条件种类表示不限制。`IsEnabled` 表示规则是否参与计划计算，停用不会丢失条件、目标或优先级。规则页面要求非空名称和相对归档子目录；条件可以全部留空，此时规则匹配全部项目。新增、启停、删除或恢复默认规则后立即丢弃当前 Draft 计划。UI 不复制匹配算法，计划仍统一通过 `OrganizationPlanner` 生成。

规划器只比较一个桌面项目命中的最高优先级规则。同优先级规则若产生相同动作与目标，会合并命中说明并生成一个计划项；若目标不同，则生成 `RuleConflict` 及候选集合，不生成该项目的可执行项。`ExecutionGate` 在计划包含任何冲突时拒绝执行整个计划，防止其他无冲突项绕过待解决冲突先行执行。

`OrganizationPlanner.ResolveConflict` 只接受草稿计划、现存冲突和该冲突候选集合内的规则 ID，调用方不能注入任意目标路径。裁决把候选转换为带原始大小和修改时间的计划项，并移除对应冲突，因此仍接受后续快照校验。裁决不写入规则设置；重新规划会重新计算冲突。规则编辑保留规则 ID 与启停状态，复制则生成新 ID，任何规则保存都会作废当前计划。

`DesktopItemDispositionPolicy` 以规范化完整路径保存保留或忽略偏好，同一路径只有一个处置，恢复到收件箱会移除偏好。保留项留在 `DesktopSnapshot`，但规划器跳过，因此 UI 可继续展示且不计入待整理数量；忽略项由 `DirectoryDesktopCatalog` 在快照和变化通知边界过滤，规划器仍做第二层跳过。偏好变更保存到 JSON、作废当前计划，并在真实桌面模式下重建文件监听器。路径重命名后不迁移偏好，避免把旧项目的用户决定错误应用到不同项目。

`FavoriteLibrary` 管理收藏夹名称唯一性和规范化完整路径成员关系，同一路径可以出现在多个收藏夹中，同一收藏夹内重复加入保持幂等。收藏夹不参与规则匹配或文件操作；UI 仅依据当前文件系统状态标记成员“可用”或“已失效”，不会因缺失、移动或重命名自动删除或迁移关系。重新绑定只替换当前收藏夹的一条旧路径，新路径已存在时合并为单一关系；批量清理只接收 UI 已确认失效的路径集合。可用成员通过系统目录下的 `explorer.exe /select` 定位，参数结构化传递且不执行成员。收藏夹与其他可移植设置一同保存，备份恢复只保留当前演示桌面或真实桌面根目录内的成员路径。

`InboxFilterCriteria` 是名称、类型、修改时间和大小的纯计算筛选边界。WPF 层保留完整的扫描/计划行集，仅把匹配行投影到列表，因此筛选切换不会重建快照、计划或规则冲突。文件夹大小为未知值，只有“全部大小”会包含文件夹。列表使用扩展多选；批量收藏逐项调用幂等的 `FavoriteLibrary` 后一次保存，批量保留/忽略逐项构造新的 `DesktopItemDispositionPolicy` 后一次保存和扫描，从而只产生一次计划失效。

## 4. 核心数据结构

```text
DesktopItem
  id
  kind: File | Folder | Shortcut
  path
  size
  createdAt
  modifiedAt
  observedVersion

Rule
  id
  name
  priority
  enabled
  conditions[]
  suggestedAction

OrganizationPlan
  id
  basedOnSnapshotVersion
  status: Draft | Confirmed | Expired
  items[]
  risks[]

PlanItem
  desktopItemId
  sourcePath
  suggestedAction
  targetPath?
  matchedRuleIds[]
  conflict?

OrganizationOperation
  id
  planId
  status: Running | PartiallyCompleted | Completed | Failed
  startedAt
  completedAt?
  items[]

OperationItem
  sourcePath
  targetPath?
  status: Pending | Succeeded | Skipped | Failed | Undone
  errorCode?
```

## 5. 关键序列

```text
ApplicationUI
  → DesktopCatalog.GetSnapshot
  → OrganizationPlanner.CreatePlan
  ← OrganizationPlan（只读预览）

用户确认

ApplicationUI
  → DesktopCatalog.GetSnapshot
  → OrganizationPlanner.Validate
  → OperationJournal.Begin
  → FileOrganizer.Execute
      → 每个项目变更后 OperationJournal.RecordResult
  → OperationJournal.Complete
  ← OrganizationOperation
```

## 6. 测试策略

- OrganizationPlanner 使用纯内存测试。
- FileOrganizer 使用测试专用临时目录，禁止指向真实桌面。
- OperationJournal 使用临时 SQLite 数据库。
- 通过 FileOrganizer 的公开接口验证结果，不测试内部辅助类。
- 至少覆盖重名、文件占用、权限失败、源文件变化、部分成功、重复撤销和异常中断。

## 7. 当前技术风险验证顺序

1. 解析真实桌面位置与 OneDrive 重定向。
2. 监听新增、删除、重命名并合并重复事件。
3. 使用测试目录完成安全移动与冲突命名。
4. 写前日志和应用重启后的恢复。
5. 托盘、全局快捷键和单实例运行。

其中 1、2 已完成自定义主监控目录与公共桌面只读叠加，高频事件通过静默窗口合并后增量应用；FileSystemWatcher 缓冲区异常会触发 Reset 完整快照。4 已在测试目录验证写前日志、独立撤销记录与中断状态对账。

2026-08-21 在本机使用 1000 个临时文件验证 `GetSnapshot()`，扫描耗时 55.9ms，低于 PRD 的 2000ms 上限。

## 8. 备份与恢复边界

`BackupPackageService` 使用三个固定根级 JSON 条目生成 `.dmbak` ZIP，并通过同目录临时文件完成原子导出。读取时只接受当前格式版本、固定条目白名单和受限条目大小，拒绝额外、重复或目录穿越条目。

`BackupRestorePlanner` 是导入与持久化之间的纯计算边界。规则归档目标必须仍位于托管目录内；项目处置偏好必须属于当前演示或真实桌面；操作项的源路径与目标路径必须同时属于对应作用域。恢复前 UI 展示应用与跳过数量，确认后才合并 SQLite 历史并替换规则、通知和全局快捷键等可移植设置。注册表开机启动状态不进入备份模型。

## 9. 诊断与隐私边界

`IDiagnosticLog` 与操作日志分离：前者记录应用生命周期、系统集成警告、整理结果摘要和异常，后者仍是可撤销文件操作的事实记录。`FileDiagnosticLog` 使用 `%LOCALAPPDATA%\DesktopManager\Logs` 下的按日 JSONL 文件并保留 7 天；写入失败不会阻止主应用运行。

所有诊断消息在写盘时先由 `DiagnosticPrivacy` 替换已知个人目录前缀，并把其他盘符绝对路径和 UNC 路径替换为 `[PATH]`；导出时再次执行同一脱敏。`DiagnosticBundleService` 原子生成只包含环境摘要、最近事件和隐私说明的 ZIP，不读取设置 JSON、SQLite 或桌面项目内容。

## 10. 桌面收纳窗口

`CollectionZoneCatalog` 把规则的规范化相对目标目录聚合为收纳区，并从目录生成稳定标识。规则仍负责匹配和建议，收纳区负责表达实际存储位置；多条规则指向同一目录时不会重复创建窗口。

`CollectionZoneStorage` 是直接文件操作的深模块。调用方只读取直接子项，或请求拖入、复制粘贴、重命名、移出和回收站删除；实现内部处理路径规范化、区域约束、同名安全命名、文件/目录差异和跨卷移动。复制接口保留源项目、拒绝重解析点目录树，并由界面在线程池执行，避免大型文件夹阻塞 WPF 线程。集成测试通过同一接口验证可观察的文件系统结果。

`CollectionWindowCoordinator` 负责规则与桌面窗口生命周期的同步。它创建、逐个隐藏、重排和关闭窗口，并把布局变化收敛为 `CollectionWindowsPreferences`；不存在桌面窗口总启用状态。`CollectionWindow` 只处理单个收纳区的呈现、文件监听和交互。规则编辑与窗口管理共用“规则与收纳”导航入口。

`CollectionWindowLayoutSolver` 区分移动完成、实时缩放和缩放完成三种输入。移动期间不参与求解，松开后才消除重叠并按左侧/上侧优先级匹配邻窗高度或宽度；上下邻窗同时构成边界时，求解器会让当前窗口填满二者之间的可用高度。实时缩放只匹配相邻窗口尺寸，完成后的碰撞约束只能改动用户正在拖拽的边。原生 `WM_SIZING` 矩形是缩放期间唯一写入源，未调整边始终作为固定锚点。

`CollectionWindow` 维护收纳区根目录和当前浏览目录两层状态。文件夹双击只允许进入根目录后代中的普通目录，递归文件监听负责刷新当前层；返回导航不能越过收纳区根目录。`CollectionZoneStorage` 的重命名、移出和回收站操作允许处理根目录内的嵌套项目，但仍拒绝根目录以外的路径。首字母定位由无界面依赖的 `CollectionItemTypeAhead` 计算循环匹配索引。窗口内排序使用进程内拖拽格式与系统 `FileDrop` 并存：同目录放下时只重排 `ObservableCollection`，跨窗口或拖出时仍执行系统文件拖放。每个收纳区、每层相对目录的名称顺序保存在 `CollectionWindowsPreferences.ItemOrders`，`CollectionItemOrderResolver` 负责把新增项目稳定追加到手动顺序之后；`CollectionItemQuickSorter` 统一实现名称、大小、文件后缀类别和修改时间排序，其中类别按后缀 A–Z 分组、组内按名称 A–Z，文件夹优先且无后缀项目最后。界面只负责应用并持久化结果。空白区域在预览鼠标阶段清除选中并打开快速排序菜单，文件项目自身继续使用项目操作菜单。

`DesktopWidgetCoordinator` 是三种唯一特殊窗口的生命周期与布局 seam。调用方只同步 `DesktopWidgetsPreferences`、切换启用状态或请求显示/隐藏；实现内部负责创建快速应用、日历和待办事项窗口，采集退出前最终状态，并把它们与收纳窗口共同交给 `CollectionWindowLayoutSolver`。待办事项由 `TodoWindowDefinition` 原子保存任务与布局；旧配置中的优先级和筛选字段由 JSON 兼容忽略。`TodoItemQuery` 统一封装未完成/已完成分区、截止日期和完成时间排序；WPF 窗口把顶部日期轨作为导航与新增日期输入，只投影当前日期的事项，以红点提示日期是否有内容，并用 `ObservableCollection` 原生分组承载未完成/已完成状态。

桌面收纳窗口采用独立、无任务栏项、非置顶的 WPF 伴随窗口，普通应用可以自然覆盖它。实现不把窗口挂接到 Explorer 的 `WorkerW/Progman` 私有窗口树，避免 Explorer 重启和 Windows 更新破坏窗口生命周期。窗口通过 `SHGetFileInfo` 按实际路径提取 Windows Shell 原始图标，并立即复制为 WPF 位图后释放原生句柄。窗口支持 Windows 高对比度、工作区坐标修正和 350ms 文件变化合并刷新。
