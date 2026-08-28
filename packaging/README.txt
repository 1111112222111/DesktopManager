桌面管理 · Windows 发行包

安装
1. 解压整个 ZIP，不能只单独运行其中一个文件。
2. 双击 Install.cmd。
3. 默认安装到当前用户的 LocalAppData\Programs\DesktopManager，不需要管理员权限。

升级
下载并解压新版本，再次双击 Install.cmd。安装器会原位替换程序文件；
%LOCALAPPDATA%\DesktopManager 中的设置和操作历史不会被修改。

桌面收纳窗口
保存托管目录和规则后，应用会按不同归档子目录创建桌面收纳窗口。
相同目标目录的规则共用一个窗口。可直接拖入或拖出文件；双击打开，右键可定位、
重命名、移动到其他目录或移入 Windows 回收站。主窗口“收纳窗口”页可统一显示、
隐藏、自动排列，并修改窗口名称和颜色。

卸载
先从托盘菜单退出应用，再通过 Windows“已安装的应用”中的“桌面管理”卸载，
或运行安装目录中的 Uninstall.cmd。默认保留设置和操作历史。

彻底清理用户数据（不可恢复）
在 PowerShell 中运行：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -RemoveUserData

安全说明
- 卸载会清理“桌面管理”的当前用户开机启动项和开始菜单快捷方式。
- 本软件仅供本机使用，发行包不进行代码签名或时间戳验证；Windows 可能显示“未知发布者”或 SmartScreen 提示。
- 校验 ZIP 完整性时，请与同名 .sha256 文件中的 SHA256 值比较。
