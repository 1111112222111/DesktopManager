using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopManager.Core;
using DesktopManager.Infrastructure;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace DesktopManager.App;

public partial class MainWindow : Window
{
    public event Action<int>? InboxCountChanged;
    public event Action<string, string>? NotificationRequested;
    public event Action<GlobalHotKeyBinding>? GlobalHotKeyChangeRequested;

    public NotificationPreferences CurrentNotificationPreferences { get; private set; } =
        NotificationPreferences.Default;
    public GlobalHotKeyBinding CurrentGlobalHotKeyBinding { get; private set; } =
        GlobalHotKeyBinding.Default;

    private readonly string _demoRoot = Path.Combine(Path.GetTempPath(), "DesktopManager.Demo");
    private readonly ObservableCollection<PreviewRow> _rows = [];
    private readonly List<PreviewRow> _allRows = [];
    private readonly ObservableCollection<HistoryRow> _historyRows = [];
    private readonly ObservableCollection<HistoryItemRow> _historyItemRows = [];
    private readonly ObservableCollection<RuleRow> _ruleRows = [];
    private readonly ObservableCollection<ItemPreferenceRow> _itemPreferenceRows = [];
    private readonly ObservableCollection<FavoriteCollectionRow> _favoriteRows = [];
    private readonly ObservableCollection<FavoriteMemberRow> _favoriteMemberRows = [];
    private readonly ObservableCollection<CollectionWindowRow> _collectionWindowRows = [];
    private readonly IOperationJournal _demoOperationJournal;
    private readonly IOperationJournal _realOperationJournal;
    private readonly OperationHistory _operationHistory;
    private readonly JsonAppSettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly CollectionWindowCoordinator _collectionWindows;
    private readonly DesktopWidgetCoordinator _desktopWidgets;
    private AppSettings _savedSettings = new();
    private DesktopItemDispositionPolicy _dispositionPolicy = DesktopItemDispositionPolicy.Empty;
    private FavoriteLibrary _favoriteLibrary = FavoriteLibrary.Empty;
    private DesktopSnapshot? _snapshot;
    private OrganizationPlan? _currentPlan;
    private HistoryRow? _lastRecoverableOperation;
    private IDisposable? _watchSubscription;
    private DesktopChangeBatcher? _changeBatcher;
    private Guid? _editingRuleId;
    private string _activeSourceDirectory;
    private bool _isRealDesktopMode;
    private bool _isExecutingOrganization;
    private string _collectionWindowStartColor = "#232B28";
    private string _collectionWindowEndColor = "#151B19";
    private readonly List<Action> _highContrastRestorers = [];

    private string DemoSourceDirectory => Path.Combine(_demoRoot, "Desktop");
    private string ManagedDirectory => Path.Combine(_demoRoot, "Managed");
    private string DemoOperationDatabasePath => Path.Combine(_demoRoot, "operations.db");
    private static string RealOperationDatabasePath => Path.Combine(
        AppDataLocation.Root,
        "real-operations.db");
    internal static string SettingsFilePath => Path.Combine(
        AppDataLocation.Root,
        "settings.json");
    internal bool AreCollectionWindowsDesktopHosted =>
        _collectionWindows.AreVisibleWindowsDesktopHosted && _desktopWidgets.AreVisibleWindowsDesktopHosted;

    public MainWindow(IDiagnosticLog diagnosticLog)
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        _diagnosticLog = diagnosticLog;
        _activeSourceDirectory = DemoSourceDirectory;
        _demoOperationJournal = new SqliteOperationJournal(DemoOperationDatabasePath);
        _realOperationJournal = new SqliteOperationJournal(RealOperationDatabasePath);
        _operationHistory = new OperationHistory(
            new OperationJournalSource(OperationScope.Demo, _demoOperationJournal),
            new OperationJournalSource(OperationScope.RealDesktop, _realOperationJournal));
        _settingsStore = new JsonAppSettingsStore(SettingsFilePath);
        _startupRegistration = new WindowsStartupRegistration(
            new CurrentUserRunStartupValueStore(),
            Path.Combine(AppContext.BaseDirectory, "DesktopManager.App.exe"));
        _collectionWindows = new CollectionWindowCoordinator();
        _desktopWidgets = new DesktopWidgetCoordinator();
        _collectionWindows.ExternalObstaclesProvider = _desktopWidgets.GetVisibleRectangles;
        _desktopWidgets.ExternalObstaclesProvider = _collectionWindows.GetVisibleRectangles;
        InitializeComponent();
        PlanList.ItemsSource = _rows;
        HistoryList.ItemsSource = _historyRows;
        HistoryItemList.ItemsSource = _historyItemRows;
        RulesList.ItemsSource = _ruleRows;
        ItemPreferencesList.ItemsSource = _itemPreferenceRows;
        FavoritesList.ItemsSource = _favoriteRows;
        FavoriteMembersList.ItemsSource = _favoriteMemberRows;
        CollectionWindowsList.ItemsSource = _collectionWindowRows;
        InboxFavoriteCombo.ItemsSource = _favoriteRows;
        SourcePathText.Text = $"扫描位置：{_activeSourceDirectory}";
        ManagedPathText.Text = $"归档位置：{ManagedDirectory}";
        _collectionWindows.PreferencesChanged += CollectionWindows_PreferencesChanged;
        _collectionWindows.SummariesChanged += RefreshCollectionWindowRows;
        _collectionWindows.OperationCompleted += message =>
        {
            CollectionWindowsStatusText.Text = message;
            _diagnosticLog.Write(DiagnosticLevel.Information, "CollectionWindows", message);
        };
        _desktopWidgets.PreferencesChanged += preferences =>
        {
            _savedSettings = _savedSettings with { DesktopWidgets = preferences };
            _settingsStore.Save(_savedSettings);
            UpdateDesktopWidgetStatus();
        };
        Loaded += MainWindow_Loaded;
    }

    private void PrepareDemo_Click(object sender, RoutedEventArgs e)
    {
        StopWatching();
        _isRealDesktopMode = false;
        _activeSourceDirectory = DemoSourceDirectory;
        ModeBannerText.Text = "技术验证模式 · 仅操作系统临时目录 · 不会访问真实桌面";
        SourcePathText.Text = $"扫描位置：{_activeSourceDirectory}";
        ManagedPathText.Text = $"归档位置：{ManagedDirectory}";
        CreatePlanButton.Content = "生成整理计划";
        CreatePlanButton.IsEnabled = true;
        UpdateActiveUndoAvailability();
        Directory.CreateDirectory(DemoSourceDirectory);
        CreateDemoFile("会议纪要.txt", "桌面管理纵向切片演示文档");
        CreateDemoFile("屏幕截图.png", "PNG placeholder for safe demo");
        CreateDemoFile("设计素材.zip", "ZIP placeholder for safe demo");
        CreateDemoFile("暂未分类.bin", "Unmatched placeholder");
        StatusText.Text = "已在系统临时目录准备 4 个演示文件。";
        Scan();
    }

    private void RealDesktop_Click(object sender, RoutedEventArgs e)
    {
        StopWatching();
        _isRealDesktopMode = true;
        _activeSourceDirectory = GetConfiguredMonitoredDirectory();
        ModeBannerText.Text = "真实桌面整理模式 · 自动监听变化 · 预检通过后直接执行";
        SourcePathText.Text = $"只读扫描：{_activeSourceDirectory}";
        CreatePlanButton.Content = "生成真实预览";
        ExecuteButton.IsEnabled = false;
        UndoButton.IsEnabled = false;
        Scan(clearStatus: false);
        ConfigureRealDesktopPreviewAvailability();
        UpdateActiveUndoAvailability();
        StartWatchingRealDesktop();
        StatusText.Text = CreatePlanButton.IsEnabled
            ? "真实桌面扫描完成，可以生成整理计划并直接执行。"
            : "真实桌面扫描完成；请先在设置中保存有效托管目录。";
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => Scan();

    private void CreatePlan_Click(object sender, RoutedEventArgs e)
    {
        _snapshot ??= GetActiveSnapshot();
        var managedDirectory = ManagedDirectory;
        if (_isRealDesktopMode)
        {
            if (!CanCreateRealDesktopPreview(out var validationMessage))
            {
                CreatePlanButton.IsEnabled = false;
                StatusText.Text = validationMessage;
                return;
            }

            managedDirectory = _savedSettings.ManagedDirectory!;
        }

        _currentPlan = OrganizationPlanner.CreatePlan(
            _snapshot.Items.Where(item => !ContainsRunningApplication(item)).ToArray(),
            _ruleRows.Select(row => row.Rule).ToArray(),
            managedDirectory,
            dispositionPolicy: _dispositionPolicy);
        ShowPlan(_snapshot, _currentPlan);
        UpdateCurrentPlanPresentationStatus(resolutionApplied: false);
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecutingOrganization
            || _currentPlan is null
            || _currentPlan.Items.Count == 0
            || _currentPlan.Conflicts.Count > 0)
        {
            return;
        }

        var sourceDirectory = DemoSourceDirectory;
        var targetDirectory = ManagedDirectory;
        var operationJournal = _demoOperationJournal;
        var operationScope = OperationScope.Demo;
        if (_isRealDesktopMode)
        {
            if (!TryGetRealOperationContext(
                    out sourceDirectory,
                    out targetDirectory,
                    out var contextMessage))
            {
                ExecuteButton.IsEnabled = false;
                StatusText.Text = contextMessage;
                return;
            }

            operationJournal = _realOperationJournal;
            operationScope = OperationScope.RealDesktop;
        }

        var currentSnapshot = GetActiveSnapshot();
        var review = ExecutionGate.Review(_currentPlan, currentSnapshot);
        if (!review.CanExecute)
        {
            _currentPlan = _currentPlan with { Status = PlanStatus.Expired };
            ExecuteButton.IsEnabled = false;
            StatusText.Text = $"整理计划已过期：发现 {review.Validation.Issues.Count} 个源项目变化，请重新扫描并生成计划。";
            return;
        }

        var organizer = new FileOrganizer(
            operationJournal,
            sourceDirectory,
            targetDirectory);
        SetOrganizationBusy(true);
        StatusText.Text = "正在检查并收纳文件，请勿关闭软件…";
        try
        {
            var confirmedPlan = ExecutionGate.PrepareForExecution(review);
            var operation = await organizer.ExecuteAsync(confirmedPlan);
            UndoButton.IsEnabled = operation.Items.Any(item => item.Status is OperationItemStatus.Succeeded);
            StatusText.Text = operation.Status is OperationStatus.Completed
                ? $"{(operationScope is OperationScope.Demo ? "演示" : "真实桌面")}整理完成：{operation.Items.Length} 项成功，操作记录 {operation.Id:N}。"
                : "整理部分完成，请检查操作结果。";
            NotificationRequested?.Invoke("桌面整理完成", StatusText.Text);
            _diagnosticLog.Write(
                operation.Status is OperationStatus.Completed ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
                "Organization",
                $"{operationScope} 整理结束：状态 {operation.Status}，项目 {operation.Items.Length}。");
            Scan(clearStatus: false);
            await LoadHistoryAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"收纳未完成：{exception.Message}";
            _diagnosticLog.Write(
                DiagnosticLevel.Error,
                "Organization",
                $"{operationScope} 整理异常：{exception}");
        }
        finally
        {
            SetOrganizationBusy(false);
        }
    }

    private void SetOrganizationBusy(bool isBusy)
    {
        _isExecutingOrganization = isBusy;
        ExecuteButton.Content = isBusy ? "正在收纳…" : "立即执行";
        ExecuteButton.IsEnabled = !isBusy
            && _currentPlan is { Items.Count: > 0, Conflicts.Count: 0 };
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRecoverableOperation is not { } operation || !CanUndoHistoryRow(operation))
        {
            return;
        }

        var request = CreateBatchUndoRequest(operation);
        if (request is not null)
        {
            await UndoOperationAsync(operation, request);
        }
    }

    private void Scan(bool clearStatus = true)
    {
        _snapshot = GetActiveSnapshot();
        PresentSnapshot(clearStatus);
    }

    private void PresentSnapshot(bool clearStatus)
    {
        if (_snapshot is null)
        {
            return;
        }
        _currentPlan = null;
        _allRows.Clear();
        foreach (var item in _snapshot.Items.OrderBy(item => item.Path))
        {
            var isKept = _dispositionPolicy.GetDisposition(item.Path)
                is DesktopItemDisposition.Keep;
            _allRows.Add(new PreviewRow(
                item.Id,
                item.Path,
                Path.GetFileName(item.Path),
                ToDisplayKind(item.Kind),
                item.Kind,
                item.Size,
                item.ModifiedAt,
                item.IsReadOnly ? "公共桌面只读" : isKept ? "已保留" : "待生成",
                "—",
                item.IsReadOnly
                    ? "只参与展示与规则影响预览，不进入整理计划"
                    : isKept ? "保留在桌面，不参与规则建议" : "—",
                false));
        }
        ApplyInboxFilters(updateStatus: false);

        var inboxItemCount = _snapshot.Items.Count(item =>
            !item.IsReadOnly
            && _dispositionPolicy.GetDisposition(item.Path) is DesktopItemDisposition.Inbox);
        InboxCount.Text = inboxItemCount.ToString();
        InboxCountChanged?.Invoke(inboxItemCount);
        PlanCount.Text = "0";
        PlanSizeText.Text = "0 B";
        RiskCount.Text = "0 / 0";
        PlanSummaryText.Text = "尚未生成整理计划。";
        TargetDistributionText.Text = string.Empty;
        PlanRiskDetailsText.Text = string.Empty;
        ExecuteButton.IsEnabled = false;
        ResolveConflictButton.IsEnabled = false;
        ExcludePlanItemsButton.IsEnabled = false;
        KeepOnlyPlanItemsButton.IsEnabled = false;
        AdjustPlanTargetButton.IsEnabled = false;
        KeepItemButton.IsEnabled = false;
        IgnoreItemButton.IsEnabled = false;
        AddToFavoriteButton.IsEnabled = false;
        if (clearStatus)
        {
            StatusText.Text = $"扫描完成：发现 {_snapshot.Items.Count} 个项目，未执行文件变更。";
        }
        UpdateRuleImpactPreview((RulesList.SelectedItem as RuleRow)?.Rule);
    }

    private void InboxFilter_Changed(object sender, EventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }
        ApplyInboxFilters(updateStatus: true);
    }

    private void ResetInboxFilters_Click(object sender, RoutedEventArgs e)
    {
        InboxSearchText.Clear();
        InboxKindFilter.SelectedIndex = 0;
        InboxModifiedFilter.SelectedIndex = 0;
        InboxCreatedFilter.SelectedIndex = 0;
        InboxSizeFilter.SelectedIndex = 0;
        ApplyInboxFilters(updateStatus: true);
    }

    private void ApplyInboxFilters(bool updateStatus)
    {
        var criteria = GetInboxFilterCriteria();
        var now = DateTimeOffset.Now;
        var itemsById = _snapshot?.Items.ToDictionary(item => item.Id)
            ?? new Dictionary<Guid, DesktopItem>();
        var visibleRows = _allRows.Where(row =>
            itemsById.TryGetValue(row.DesktopItemId, out var item)
            && criteria.Matches(item, now));
        _rows.Clear();
        foreach (var row in visibleRows)
        {
            _rows.Add(row);
        }
        if (updateStatus)
        {
            StatusText.Text = $"筛选结果：显示 {_rows.Count} / {_allRows.Count} 个项目；未改变扫描快照或整理计划。";
        }
    }

    private InboxFilterCriteria GetInboxFilterCriteria()
    {
        var kind = (InboxKindFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "File" => DesktopItemKind.File,
            "Folder" => DesktopItemKind.Folder,
            "Shortcut" => DesktopItemKind.Shortcut,
            _ => (DesktopItemKind?)null
        };
        _ = Enum.TryParse(
            (InboxModifiedFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            out InboxModifiedFilter modified);
        _ = Enum.TryParse(
            (InboxSizeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            out InboxSizeFilter size);
        _ = Enum.TryParse(
            (InboxCreatedFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            out InboxCreatedFilter created);
        return new InboxFilterCriteria(InboxSearchText.Text, kind, modified, size, created);
    }

    private void SelectAllVisible_Click(object sender, RoutedEventArgs e)
    {
        PlanList.SelectAll();
        StatusText.Text = $"已选择当前筛选结果中的 {PlanList.SelectedItems.Count} 个项目。";
    }

    private PreviewRow[] GetSelectedPreviewRows() =>
        PlanList.SelectedItems.Cast<PreviewRow>().ToArray();

    private void ShowPlan(DesktopSnapshot snapshot, OrganizationPlan plan)
    {
        var plannedBySource = plan.Items.ToDictionary(item => item.SourcePath, StringComparer.OrdinalIgnoreCase);
        var conflictsBySource = plan.Conflicts.ToDictionary(
            conflict => conflict.SourcePath,
            StringComparer.OrdinalIgnoreCase);
        _allRows.Clear();
        foreach (var item in snapshot.Items.OrderBy(item => item.Path))
        {
            if (item.IsReadOnly)
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "公共桌面只读",
                    "—",
                    "可预览规则影响，但不进入整理计划",
                    false));
            }
            else if (_dispositionPolicy.GetDisposition(item.Path) is DesktopItemDisposition.Keep)
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "已保留",
                    "—",
                    "保留在桌面，不参与规则建议",
                    false));
            }
            else if (plannedBySource.TryGetValue(item.Path, out var planItem))
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "归档",
                    planItem.TargetPath,
                    planItem.Explanation,
                    false));
            }
            else if (conflictsBySource.TryGetValue(item.Path, out var conflict))
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "规则冲突",
                    string.Join(" | ", conflict.Choices.Select(choice => choice.TargetPath)),
                    string.Join("、", conflict.Choices.Select(choice => choice.RuleName)),
                    true));
            }
            else if (ContainsRunningApplication(item))
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "保留原位",
                    "—",
                    "包含当前正在运行的软件，不参与收纳",
                    false));
            }
            else if (plan.ExcludedItemIds.Contains(item.Id))
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "已从计划排除",
                    "—",
                    "当前草稿不执行；重新生成计划可恢复",
                    false));
            }
            else
            {
                _allRows.Add(new PreviewRow(
                    item.Id,
                    item.Path,
                    Path.GetFileName(item.Path),
                    ToDisplayKind(item.Kind),
                    item.Kind,
                    item.Size,
                    item.ModifiedAt,
                    "暂不处理",
                    "—",
                    "未命中规则",
                    false));
            }
        }
        ApplyInboxFilters(updateStatus: false);

        PlanCount.Text = plan.Items.Count.ToString();
        UpdatePlanSummary(plan);
        ResolveConflictButton.IsEnabled = false;
        ExcludePlanItemsButton.IsEnabled = false;
        KeepOnlyPlanItemsButton.IsEnabled = false;
        AdjustPlanTargetButton.IsEnabled = false;
        KeepItemButton.IsEnabled = false;
        IgnoreItemButton.IsEnabled = false;
        AddToFavoriteButton.IsEnabled = false;
    }

    private void UpdatePlanSummary(OrganizationPlan plan)
    {
        var summary = OrganizationPlanAnalyzer.Summarize(plan);
        PlanSizeText.Text = FormatPlanSize(summary.KnownTotalSizeBytes);
        RiskCount.Text = $"{summary.ConflictCount} / {summary.ExcludedItemCount}";
        PlanSummaryText.Text = $"可执行 {summary.ExecutableItemCount} 项 · 已排除 {summary.ExcludedItemCount} 项 · "
            + $"冲突 {summary.ConflictCount} 项 · 已知大小 {FormatPlanSize(summary.KnownTotalSizeBytes)}"
            + (summary.UnknownSizeItemCount > 0
                ? $" · {summary.UnknownSizeItemCount} 个文件夹大小未知"
                : string.Empty);

        var displayedTargets = summary.TargetDistribution.Take(4).Select(target =>
            $"• {target.TargetDirectory}：{target.ItemCount} 项，已知 {FormatPlanSize(target.KnownSizeBytes)}"
            + (target.UnknownSizeItemCount > 0
                ? $"，{target.UnknownSizeItemCount} 个文件夹未知"
                : string.Empty));
        TargetDistributionText.Text = summary.TargetDistribution.Count == 0
            ? "目标分布：当前没有可执行项。"
            : "目标分布：\n" + string.Join("\n", displayedTargets)
                + (summary.TargetDistribution.Count > 4
                    ? $"\n• 另有 {summary.TargetDistribution.Count - 4} 个目标目录"
                    : string.Empty);

        var risks = new List<string>();
        if (summary.ConflictCount > 0)
        {
            risks.Add($"{summary.ConflictCount} 个规则冲突会阻止执行");
        }
        if (summary.ExcludedItemCount > 0)
        {
            risks.Add($"{summary.ExcludedItemCount} 项只在当前草稿中被排除");
        }
        if (summary.UnknownSizeItemCount > 0)
        {
            risks.Add($"{summary.UnknownSizeItemCount} 个文件夹未递归计算大小");
        }
        if (summary.DuplicateTargetCount > 0)
        {
            risks.Add($"{summary.DuplicateTargetCount} 项与其他计划项目标同名，执行时会安全改名");
        }
        try
        {
            var preflight = CreateOrganizerForCurrentMode().Inspect(plan);
            risks.AddRange(preflight.Issues
                .Where(issue => issue.Kind is not PreflightIssueKind.DuplicateTarget
                    and not PreflightIssueKind.UnknownFolderSize)
                .Select(issue =>
                    $"{(issue.Severity is PreflightIssueSeverity.Blocking ? "阻断" : "警告")}：{issue.Message}"));
        }
        catch (InvalidOperationException exception)
        {
            risks.Add($"阻断：{exception.Message}");
        }
        PlanRiskDetailsText.Text = risks.Count == 0
            ? "风险：未发现阻断性问题；执行前仍会重新校验源项目。"
            : "风险：" + string.Join("；", risks) + "。";
    }

    private FileOrganizer CreateOrganizerForCurrentMode() => new(
        _isRealDesktopMode ? _realOperationJournal : _demoOperationJournal,
        _isRealDesktopMode ? _activeSourceDirectory : DemoSourceDirectory,
        _isRealDesktopMode ? _savedSettings.ManagedDirectory! : ManagedDirectory);

    private static string FormatPlanSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.#} GB"
    };

    private static bool ContainsRunningApplication(DesktopItem item)
    {
        if (item.Kind is not DesktopItemKind.Folder)
        {
            return false;
        }
        var folder = Path.GetFullPath(item.Path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var applicationPath = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(folder, applicationPath, StringComparison.OrdinalIgnoreCase)
            || applicationPath.StartsWith(
                folder + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private void PlanList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedRows = GetSelectedPreviewRows();
        var plannedIds = _currentPlan?.Items
            .Select(item => item.DesktopItemId)
            .ToHashSet() ?? [];
        var allSelectedRowsArePlanItems = selectedRows.Length > 0
            && selectedRows.All(row => plannedIds.Contains(row.DesktopItemId));
        ResolveConflictButton.IsEnabled = _currentPlan is not null
            && selectedRows is [{ IsConflict: true }];
        ExcludePlanItemsButton.IsEnabled = allSelectedRowsArePlanItems;
        KeepOnlyPlanItemsButton.IsEnabled = allSelectedRowsArePlanItems
            && _currentPlan is { Conflicts.Count: 0 };
        AdjustPlanTargetButton.IsEnabled = allSelectedRowsArePlanItems
            && selectedRows.Length == 1;
        var selectedPaths = selectedRows.Select(row => row.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var containsReadOnlyItem = _snapshot?.Items.Any(item =>
            item.IsReadOnly && selectedPaths.Contains(item.Path)) is true;
        KeepItemButton.IsEnabled = selectedRows.Length > 0 && !containsReadOnlyItem;
        IgnoreItemButton.IsEnabled = selectedRows.Length > 0 && !containsReadOnlyItem;
        AddToFavoriteButton.IsEnabled = selectedRows.Length > 0
            && InboxFavoriteCombo.SelectedItem is FavoriteCollectionRow;
        KeepItemButton.Content = selectedRows is [var selected]
            && _dispositionPolicy.GetDisposition(selected.SourcePath) is DesktopItemDisposition.Keep
                ? "恢复到收件箱"
                : selectedRows.Length > 1
                    ? $"保留选中项 ({selectedRows.Length})"
                    : "保留选中项";
        IgnoreItemButton.Content = selectedRows.Length > 1
            ? $"忽略选中项 ({selectedRows.Length})"
            : "忽略选中项";
    }

    private void KeepItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = GetSelectedPreviewRows();
        if (selectedRows.Length == 0)
        {
            return;
        }

        var nextDisposition = selectedRows is [var only]
            && _dispositionPolicy.GetDisposition(only.SourcePath) is DesktopItemDisposition.Keep
                ? DesktopItemDisposition.Inbox
                : DesktopItemDisposition.Keep;
        ApplyItemDispositions(
            selectedRows.Select(row => row.SourcePath),
            nextDisposition,
            nextDisposition is DesktopItemDisposition.Keep
                ? $"已将 {selectedRows.Length} 个项目设为保留，不再参与规则建议。"
                : "项目已恢复到收件箱，可以重新生成建议。");
    }

    private void IgnoreItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = GetSelectedPreviewRows();
        if (selectedRows.Length > 0)
        {
            ApplyItemDispositions(
                selectedRows.Select(row => row.SourcePath),
                DesktopItemDisposition.Ignore,
                $"已忽略 {selectedRows.Length} 个项目并从扫描结果移除，可在设置中恢复。");
        }
    }

    private void ApplyItemDisposition(
        string path,
        DesktopItemDisposition disposition,
        string message) => ApplyItemDispositions([path], disposition, message);

    private void ApplyItemDispositions(
        IEnumerable<string> paths,
        DesktopItemDisposition disposition,
        string message)
    {
        if (_isRealDesktopMode)
        {
            StopWatching();
        }

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _dispositionPolicy = _dispositionPolicy.WithDisposition(path, disposition);
        }
        _savedSettings = _savedSettings with
        {
            ItemPreferences = _dispositionPolicy.Preferences.ToArray()
        };
        _settingsStore.Save(_savedSettings);
        LoadItemPreferences();
        Scan(clearStatus: false);
        if (_isRealDesktopMode)
        {
            StartWatchingRealDesktop();
            ConfigureRealDesktopPreviewAvailability();
        }
        StatusText.Text = message + " 当前整理计划已作废。";
    }

    private void ResolveConflict_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null
            || _snapshot is null
            || PlanList.SelectedItem is not PreviewRow { IsConflict: true } row)
        {
            return;
        }

        var conflict = _currentPlan.Conflicts.FirstOrDefault(candidate =>
            candidate.DesktopItemId == row.DesktopItemId);
        if (conflict is null)
        {
            return;
        }

        var dialog = new ConflictResolutionWindow(conflict) { Owner = this };
        if (dialog.ShowDialog() is not true || dialog.SelectedRuleId is not { } selectedRuleId)
        {
            return;
        }

        _currentPlan = OrganizationPlanner.ResolveConflict(
            _currentPlan,
            conflict.DesktopItemId,
            selectedRuleId);
        ShowPlan(_snapshot, _currentPlan);
        UpdateCurrentPlanPresentationStatus(resolutionApplied: true);
    }

    private void ExcludePlanItems_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _snapshot is null)
        {
            return;
        }

        var selectedIds = GetSelectedPreviewRows()
            .Select(row => row.DesktopItemId)
            .ToArray();
        try
        {
            _currentPlan = OrganizationPlanner.ExcludeItems(_currentPlan, selectedIds);
            ShowPlan(_snapshot, _currentPlan);
            UpdateCurrentPlanPresentationStatus(resolutionApplied: false);
            StatusText.Text = $"已从当前草稿排除 {selectedIds.Length} 项；规则和项目偏好均未改变。";
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void KeepOnlyPlanItems_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null || _snapshot is null)
        {
            return;
        }

        var selectedIds = GetSelectedPreviewRows()
            .Select(row => row.DesktopItemId)
            .ToArray();
        try
        {
            _currentPlan = OrganizationPlanner.KeepOnlyItems(_currentPlan, selectedIds);
            ShowPlan(_snapshot, _currentPlan);
            UpdateCurrentPlanPresentationStatus(resolutionApplied: false);
            StatusText.Text = $"当前草稿仅保留 {selectedIds.Length} 个执行项；重新生成计划可恢复完整建议。";
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void AdjustPlanTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null
            || _snapshot is null
            || GetSelectedPreviewRows() is not [var selectedRow])
        {
            return;
        }

        var planItem = _currentPlan.Items.FirstOrDefault(item =>
            item.DesktopItemId == selectedRow.DesktopItemId);
        if (planItem is null)
        {
            return;
        }

        var managedDirectory = _isRealDesktopMode
            ? _savedSettings.ManagedDirectory!
            : ManagedDirectory;
        var currentDirectory = Path.GetDirectoryName(planItem.TargetPath) ?? managedDirectory;
        var currentRelativeDestination = Path.GetRelativePath(managedDirectory, currentDirectory);
        var dialog = new PlanTargetWindow(selectedRow.Name, currentRelativeDestination)
        {
            Owner = this
        };
        if (dialog.ShowDialog() is not true || dialog.RelativeDestination is not { } destination)
        {
            return;
        }

        try
        {
            _currentPlan = OrganizationPlanner.AdjustTarget(
                _currentPlan,
                planItem.DesktopItemId,
                destination,
                managedDirectory);
            ShowPlan(_snapshot, _currentPlan);
            UpdateCurrentPlanPresentationStatus(resolutionApplied: false);
            StatusText.Text = "已修改当前草稿的归档目标；文件名、整理规则和磁盘内容均未改变。";
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                exception.Message,
                "无法修改目标",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateCurrentPlanPresentationStatus(bool resolutionApplied)
    {
        if (_currentPlan is null)
        {
            ExecuteButton.IsEnabled = false;
            return;
        }

        ExecuteButton.IsEnabled = !_isExecutingOrganization
            && _currentPlan.Items.Count > 0
            && _currentPlan.Conflicts.Count == 0;
        if (_currentPlan.Conflicts.Count > 0)
        {
            StatusText.Text = resolutionApplied
                ? $"裁决已应用；仍有 {_currentPlan.Conflicts.Count} 个规则冲突，当前不可执行。"
                : $"检测到 {_currentPlan.Conflicts.Count} 个规则冲突；可逐项裁决或修改规则后重新生成计划。";
            return;
        }

        var status = _isRealDesktopMode
            ? GetRealPreviewStatus(_currentPlan)
            : $"已生成只读计划：{_currentPlan.Items.Count} 项可直接执行，尚未移动任何文件。";
        StatusText.Text = resolutionApplied ? "全部规则冲突已解决；" + status : status;
    }

    private void CreateDemoFile(string fileName, string contents)
    {
        var path = Path.Combine(DemoSourceDirectory, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, contents);
        }
    }

    private static string ToDisplayKind(DesktopItemKind kind) => kind switch
    {
        DesktopItemKind.File => "文件",
        DesktopItemKind.Folder => "文件夹",
        DesktopItemKind.Shortcut => "快捷方式",
        _ => "未知"
    };

    private void RefreshAfterDesktopChanges(IReadOnlyList<DesktopChange> changes)
    {
        _snapshot = _snapshot is null
            ? GetActiveSnapshot()
            : CreateRealDesktopCatalog().ApplyChanges(_snapshot, changes);
        PresentSnapshot(clearStatus: false);
        if (!_isExecutingOrganization)
        {
            StatusText.Text = $"增量处理 {changes.Count} 个桌面变化，当前显示 {_snapshot.Items.Count} 个项目。";
        }
        NotificationRequested?.Invoke(
            "桌面收件箱已更新",
            $"检测到 {changes.Count} 个变化，当前可整理 {InboxCount.Text} 项。");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        ApplyAccessibilityTheme();
        Loaded -= MainWindow_Loaded;
        var demoRecovered = await new FileOrganizer(
            _demoOperationJournal,
            DemoSourceDirectory,
            ManagedDirectory).RecoverInterruptedAsync();
        LoadSettings();
        var realRecoveredCount = 0;
        if (TryValidateManagedDirectory(_savedSettings.ManagedDirectory, out _))
        {
            var realRecovered = await new FileOrganizer(
                _realOperationJournal,
                GetConfiguredMonitoredDirectory(),
                _savedSettings.ManagedDirectory!).RecoverInterruptedAsync();
            realRecoveredCount = realRecovered.Count;
        }

        await LoadHistoryAsync();
        var recoveredCount = demoRecovered.Count + realRecoveredCount;
        if (recoveredCount > 0)
        {
            HistoryStatusText.Text = $"已安全对账 {recoveredCount} 个中断操作；未自动继续移动文件。";
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.F5 && Keyboard.Modifiers is ModifierKeys.None)
        {
            Scan();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.F && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ShowInbox_Click(this, new RoutedEventArgs());
            InboxSearchText.Focus();
            InboxSearchText.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Escape && PlanList.SelectedItems.Count > 0)
        {
            PlanList.UnselectAll();
            e.Handled = true;
        }
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Dispatcher.InvokeAsync(ApplyAccessibilityTheme);
        }
    }

    private void ApplyAccessibilityTheme()
    {
        RestoreAccessibilityTheme();
        if (!SystemParameters.HighContrast)
        {
            return;
        }
        ApplyHighContrastRecursive(this);
    }

    private void ApplyHighContrastRecursive(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock text:
                OverrideForHighContrast(text, TextBlock.ForegroundProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
            case System.Windows.Controls.Control control:
                OverrideForHighContrast(control, System.Windows.Controls.Control.ForegroundProperty, System.Windows.SystemColors.ControlTextBrush);
                OverrideForHighContrast(control, System.Windows.Controls.Control.BackgroundProperty, System.Windows.SystemColors.ControlBrush);
                OverrideForHighContrast(control, System.Windows.Controls.Control.BorderBrushProperty, System.Windows.SystemColors.ControlTextBrush);
                break;
            case Border border:
                OverrideForHighContrast(border, Border.BackgroundProperty, System.Windows.SystemColors.WindowBrush);
                OverrideForHighContrast(border, Border.BorderBrushProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
        }

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); index++)
        {
            ApplyHighContrastRecursive(System.Windows.Media.VisualTreeHelper.GetChild(element, index));
        }
    }

    private void OverrideForHighContrast(
        DependencyObject element,
        DependencyProperty property,
        object value)
    {
        var original = element.ReadLocalValue(property);
        _highContrastRestorers.Add(() =>
        {
            if (original == DependencyProperty.UnsetValue)
            {
                element.ClearValue(property);
            }
            else
            {
                element.SetValue(property, original);
            }
        });
        element.SetValue(property, value);
    }

    private void RestoreAccessibilityTheme()
    {
        foreach (var restore in _highContrastRestorers)
        {
            restore();
        }
        _highContrastRestorers.Clear();
    }

    private void ShowInbox_Click(object sender, RoutedEventArgs e) => OpenInbox();

    public void OpenInbox()
    {
        InboxView.Visibility = Visibility.Visible;
        FavoritesView.Visibility = Visibility.Collapsed;
        RulesView.Visibility = Visibility.Collapsed;
        CollectionWindowsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        InboxNavButton.Background = System.Windows.Media.Brushes.White;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        HistoryNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RulesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void ShowFavorites_Click(object sender, RoutedEventArgs e)
    {
        InboxView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Visible;
        RulesView.Visibility = Visibility.Collapsed;
        CollectionWindowsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        InboxNavButton.Background = System.Windows.Media.Brushes.Transparent;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.White;
        RulesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        HistoryNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RefreshFavoriteMembers();
    }

    private void ShowRules_Click(object sender, RoutedEventArgs e)
    {
        InboxView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Collapsed;
        RulesView.Visibility = Visibility.Visible;
        CollectionWindowsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        InboxNavButton.Background = System.Windows.Media.Brushes.Transparent;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RulesNavButton.Background = System.Windows.Media.Brushes.White;
        HistoryNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;
    }

    private async void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        InboxView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Collapsed;
        RulesView.Visibility = Visibility.Collapsed;
        CollectionWindowsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        InboxNavButton.Background = System.Windows.Media.Brushes.Transparent;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RulesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        HistoryNavButton.Background = System.Windows.Media.Brushes.White;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        await LoadHistoryAsync();
    }

    private void ShowCollectionWindows_Click(object sender, RoutedEventArgs e)
    {
        InboxView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Collapsed;
        RulesView.Visibility = Visibility.Collapsed;
        CollectionWindowsView.Visibility = Visibility.Visible;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        InboxNavButton.Background = System.Windows.Media.Brushes.Transparent;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RulesNavButton.Background = System.Windows.Media.Brushes.White;
        HistoryNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RefreshCollectionWindowRows();
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        InboxView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Collapsed;
        RulesView.Visibility = Visibility.Collapsed;
        CollectionWindowsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        InboxNavButton.Background = System.Windows.Media.Brushes.Transparent;
        FavoritesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        RulesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        HistoryNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.White;
        UpdateSettingsValidation();
    }

    private void ChooseManagedDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择桌面管理托管目录"
        };
        if (Directory.Exists(ManagedDirectoryText.Text))
        {
            dialog.InitialDirectory = ManagedDirectoryText.Text;
        }

        if (dialog.ShowDialog(this) is true)
        {
            ManagedDirectoryText.Text = Path.GetFullPath(dialog.FolderName);
            UpdateSettingsValidation();
        }
    }

    private void ChooseMonitoredDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择桌面管理监控位置"
        };
        if (Directory.Exists(MonitoredDirectoryText.Text))
        {
            dialog.InitialDirectory = MonitoredDirectoryText.Text;
        }
        if (dialog.ShowDialog(this) is true)
        {
            MonitoredDirectoryText.Text = Path.GetFullPath(dialog.FolderName);
            UpdateSettingsValidation();
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateManagedDirectory(out var validationMessage))
        {
            SettingsValidationText.Text = validationMessage;
            SaveSettingsButton.IsEnabled = false;
            return;
        }

        _savedSettings = _savedSettings with
        {
            ManagedDirectory = ManagedDirectoryText.Text,
            MonitoredDirectory = MonitoredDirectoryText.Text,
            IncludePublicDesktopReadOnly = IncludePublicDesktopCheck.IsChecked is true
        };
        _settingsStore.Save(_savedSettings);
        SettingsValidationText.Text = "目录设置已保存；整理计划通过预检后可直接执行。";
        if (_isRealDesktopMode)
        {
            StopWatching();
            _activeSourceDirectory = GetConfiguredMonitoredDirectory();
            SourcePathText.Text = $"只读扫描：{_activeSourceDirectory}";
            Scan(clearStatus: false);
            ConfigureRealDesktopPreviewAvailability();
            StartWatchingRealDesktop();
        }
        SynchronizeCollectionWindows();
        _desktopWidgets.Synchronize(_savedSettings.DesktopWidgets ?? new DesktopWidgetsPreferences());
        UpdateDesktopWidgetStatus();
    }

    private void LoadSettings()
    {
        _savedSettings = _settingsStore.Load();
        _dispositionPolicy = new DesktopItemDispositionPolicy(_savedSettings.ItemPreferences);
        LoadItemPreferences();
        LoadRules(_savedSettings.Rules);
        try
        {
            _favoriteLibrary = new FavoriteLibrary(_savedSettings.Favorites);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _favoriteLibrary = FavoriteLibrary.Empty;
            _diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Favorites",
                "收藏夹设置无效，已使用空集合。",
                exception);
        }
        LoadFavorites();
        ManagedDirectoryText.Text = _savedSettings.ManagedDirectory ?? string.Empty;
        MonitoredDirectoryText.Text = GetConfiguredMonitoredDirectory();
        IncludePublicDesktopCheck.IsChecked = _savedSettings.IncludePublicDesktopReadOnly;
        UpdateSettingsValidation();
        LoadStartupRegistration();
        LoadNotificationPreferences();
        LoadGlobalHotKeyPreference();
        LoadCollectionWindowAppearanceSettings();
        LoadDiagnosticSummary();
        SynchronizeCollectionWindows();
        _desktopWidgets.Synchronize(_savedSettings.DesktopWidgets ?? new DesktopWidgetsPreferences());
        UpdateDesktopWidgetStatus();
    }

    private void LoadCollectionWindowAppearanceSettings()
    {
        var appearance = (_savedSettings.CollectionWindows ?? new CollectionWindowsPreferences())
            .EffectiveAppearance;
        CollectionWindowOpacitySlider.Value = appearance.SurfaceOpacity;
        CollectionWindowAlwaysOnTopCheck.IsChecked = appearance.AlwaysOnTop;
        _collectionWindowStartColor = appearance.SurfaceColor;
        _collectionWindowEndColor = appearance.GradientEndColor;
        CollectionWindowFillModeCombo.SelectedItem = CollectionWindowFillModeCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                appearance.FillMode.ToString(),
                StringComparison.OrdinalIgnoreCase))
            ?? CollectionWindowFillModeCombo.Items[0];
        UpdateCollectionWindowAppearancePreview();
        CollectionWindowSettingsStatusText.Text = "窗口设置已加载。";
    }

    private void CollectionWindowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CollectionWindowOpacityText is null)
        {
            return;
        }
        CollectionWindowOpacityText.Text = $"{Math.Round(e.NewValue * 100):0}%";
        UpdateCollectionWindowAppearancePreview();
    }

    private void CollectionWindowFillModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCollectionWindowAppearancePreview();

    private void ChooseCollectionWindowStartColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseCollectionWindowColor(_collectionWindowStartColor, out var selected))
        {
            _collectionWindowStartColor = selected;
            UpdateCollectionWindowAppearancePreview();
        }
    }

    private void ChooseCollectionWindowEndColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseCollectionWindowColor(_collectionWindowEndColor, out var selected))
        {
            _collectionWindowEndColor = selected;
            UpdateCollectionWindowAppearancePreview();
        }
    }

    private void ReadDesktopColorsForAll_Click(object sender, RoutedEventArgs e)
    {
        if (!DesktopWallpaperPaletteProvider.TryCreateSuggestion(out var suggestion, out var error)
            || suggestion is null)
        {
            CollectionWindowSettingsStatusText.Text = error;
            return;
        }
        var isGradient = GetSelectedCollectionWindowFillMode() is CollectionWindowFillMode.Gradient;
        _collectionWindowStartColor = isGradient ? suggestion.GradientStartColor : suggestion.SolidColor;
        _collectionWindowEndColor = suggestion.GradientEndColor;
        UpdateCollectionWindowAppearancePreview();
        CollectionWindowSettingsStatusText.Text = $"已根据 {suggestion.SourceDescription} 生成适配颜色，保存后应用。";
    }

    private void UpdateCollectionWindowAppearancePreview()
    {
        if (CollectionWindowSurfacePreview is null
            || CollectionWindowFillModeCombo is null
            || CollectionWindowOpacitySlider is null)
        {
            return;
        }
        var appearance = BuildCollectionWindowAppearanceFromSettings();
        CollectionWindowMaterialRenderer.Apply(CollectionWindowSurfacePreview, appearance);
        var start = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(appearance.SurfaceColor);
        var end = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(appearance.GradientEndColor);
        CollectionWindowStartColorPreview.Background = new System.Windows.Media.SolidColorBrush(start);
        CollectionWindowEndColorPreview.Background = new System.Windows.Media.SolidColorBrush(end);
        CollectionWindowStartColorText.Text = appearance.SurfaceColor;
        CollectionWindowEndColorText.Text = appearance.GradientEndColor;
        CollectionWindowEndColorButton.Visibility = appearance.FillMode is CollectionWindowFillMode.Gradient
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyCollectionWindowSettings_Click(object sender, RoutedEventArgs e)
    {
        var appearance = BuildCollectionWindowAppearanceFromSettings();
        _collectionWindows.ApplyAppearanceToAllWindows(appearance);
        CollectionWindowSettingsStatusText.Text =
            $"已应用到所有窗口：{(appearance.FillMode is CollectionWindowFillMode.Gradient ? "渐变色" : "纯色")}，全局透明度 {appearance.SurfaceOpacity:P0}。";
    }

    private CollectionWindowAppearance BuildCollectionWindowAppearanceFromSettings() => new CollectionWindowAppearance(
        CollectionWindowOpacitySlider.Value,
        _collectionWindowStartColor,
        false,
        GetSelectedCollectionWindowFillMode(),
        _collectionWindowEndColor).Normalize();

    private CollectionWindowFillMode GetSelectedCollectionWindowFillMode() =>
        CollectionWindowFillModeCombo?.SelectedItem is ComboBoxItem item
        && string.Equals(item.Tag?.ToString(), "Gradient", StringComparison.OrdinalIgnoreCase)
            ? CollectionWindowFillMode.Gradient
            : CollectionWindowFillMode.Solid;

    private static bool TryChooseCollectionWindowColor(string currentColor, out string selectedColor)
    {
        var current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentColor);
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            selectedColor = currentColor;
            return false;
        }
        selectedColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }

    private void SynchronizeCollectionWindows()
    {
        if (string.IsNullOrWhiteSpace(_savedSettings.ManagedDirectory)
            || !Directory.Exists(_savedSettings.ManagedDirectory))
        {
            _collectionWindows.Clear();
            CollectionWindowsStatusText.Text = "请先在设置中保存一个现存的托管目录。";
            return;
        }

        try
        {
            var zones = CollectionZoneCatalog.Build(_ruleRows.Select(row => row.Rule).ToArray());
            _collectionWindows.Synchronize(
                _savedSettings.ManagedDirectory!,
                zones,
                _savedSettings.CollectionWindows ?? new CollectionWindowsPreferences());
            CollectionWindowsStatusText.Text = zones.Count == 0
                ? "尚无规则，因此没有可显示的收纳窗口。"
                : $"已映射 {zones.Count} 个收纳区；相同目标目录的规则共用一个窗口。";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            _collectionWindows.Clear();
            CollectionWindowsStatusText.Text = $"收纳窗口初始化失败：{exception.Message}";
            _diagnosticLog.Write(DiagnosticLevel.Error, "CollectionWindows", "收纳窗口初始化失败。", exception);
        }
    }

    private void CollectionWindows_PreferencesChanged(CollectionWindowsPreferences preferences)
    {
        _savedSettings = _savedSettings with { CollectionWindows = preferences };
        _settingsStore.Save(_savedSettings);
    }

    private void RefreshCollectionWindowRows()
    {
        var selectedId = (CollectionWindowsList.SelectedItem as CollectionWindowRow)?.ZoneId;
        _collectionWindowRows.Clear();
        foreach (var summary in _collectionWindows.Summaries)
        {
            _collectionWindowRows.Add(new CollectionWindowRow(
                summary.ZoneId,
                summary.IsVisible ? "显示" : "隐藏",
                summary.Name,
                summary.RelativeDirectory,
                $"{summary.RuleCount} 条",
                summary.HasEnabledRule ? "有启用规则" : "全部停用",
                summary.IsVisible));
        }
        CollectionWindowsList.SelectedItem = _collectionWindowRows.FirstOrDefault(row => row.ZoneId == selectedId);
    }

    public void ShowAllCollectionWindows() { _collectionWindows.ShowAll(); _desktopWidgets.ShowAll(); }
    public void HideAllCollectionWindows() { _collectionWindows.HideAll(); _desktopWidgets.HideAll(); }

    private void ToggleShortcutWindow_Click(object sender, RoutedEventArgs e) => _desktopWidgets.ToggleShortcut();
    private void ToggleCalendarWindow_Click(object sender, RoutedEventArgs e) => _desktopWidgets.ToggleCalendar();
    private void ToggleTodoWindow_Click(object sender, RoutedEventArgs e) => _desktopWidgets.ToggleTodo();
    private void UpdateDesktopWidgetStatus()
    {
        if (DesktopWidgetsStatusText is null || ToggleCalendarWindowButton is null || ToggleShortcutWindowButton is null || ToggleTodoWindowButton is null) return;
        DesktopWidgetsStatusText.Text = $"快速应用{(_desktopWidgets.IsShortcutEnabled ? "已启用" : "已关闭")} · 日历窗口{(_desktopWidgets.IsCalendarEnabled ? "已启用" : "已关闭")} · 待办事项{(_desktopWidgets.IsTodoEnabled ? "已启用" : "已关闭")}";
        ToggleShortcutWindowButton.Content = _desktopWidgets.IsShortcutEnabled ? "关闭快速应用" : "启用快速应用";
        ToggleCalendarWindowButton.Content = _desktopWidgets.IsCalendarEnabled ? "关闭日历窗口" : "启用日历窗口";
        ToggleTodoWindowButton.Content = _desktopWidgets.IsTodoEnabled ? "关闭待办事项" : "启用待办事项";
    }

    private void ShowAllCollectionWindows_Click(object sender, RoutedEventArgs e) => ShowAllCollectionWindows();
    private void HideAllCollectionWindows_Click(object sender, RoutedEventArgs e) => HideAllCollectionWindows();
    private void ResetCollectionWindowsLayout_Click(object sender, RoutedEventArgs e)
    {
        _collectionWindows.ResetLayout();
        _desktopWidgets.Arrange();
    }
    private void RefreshCollectionWindows_Click(object sender, RoutedEventArgs e) => _collectionWindows.RefreshAll();

    private void CollectionWindowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CollectionWindowsList.SelectedItem as CollectionWindowRow;
        ApplyCollectionWindowAppearanceButton.IsEnabled = selected is not null;
        ToggleCollectionWindowButton.IsEnabled = selected is not null;
        OpenCollectionWindowButton.IsEnabled = selected is not null;
        ToggleCollectionWindowButton.Content = selected?.IsVisible is true ? "隐藏窗口" : "显示窗口";
        if (selected is null)
        {
            CollectionWindowTitleText.Clear();
            return;
        }

        CollectionWindowTitleText.Text = selected.Name;
        var layout = (_savedSettings.CollectionWindows ?? new()).EffectiveLayouts
            .FirstOrDefault(item => item.ZoneId == selected.ZoneId);
        var color = layout?.AccentColor ?? "#AA8959";
        CollectionWindowColorCombo.SelectedItem = CollectionWindowColorCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), color, StringComparison.OrdinalIgnoreCase))
            ?? CollectionWindowColorCombo.Items[0];
    }

    private void ApplyCollectionWindowAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionWindowsList.SelectedItem is not CollectionWindowRow selected
            || CollectionWindowColorCombo.SelectedItem is not ComboBoxItem colorItem)
        {
            return;
        }
        _collectionWindows.UpdateAppearance(
            selected.ZoneId,
            CollectionWindowTitleText.Text,
            colorItem.Tag?.ToString() ?? "#AA8959");
        CollectionWindowsStatusText.Text = "窗口名称和颜色已保存。";
    }

    private void ToggleCollectionWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionWindowsList.SelectedItem is CollectionWindowRow selected)
        {
            _collectionWindows.SetZoneVisibility(selected.ZoneId, !selected.IsVisible);
        }
    }

    private void OpenCollectionWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionWindowsList.SelectedItem is CollectionWindowRow selected)
        {
            _collectionWindows.OpenZone(selected.ZoneId);
        }
    }

    private void LoadItemPreferences()
    {
        _itemPreferenceRows.Clear();
        foreach (var preference in _dispositionPolicy.Preferences)
        {
            _itemPreferenceRows.Add(new ItemPreferenceRow(
                preference.Path,
                preference.Disposition is DesktopItemDisposition.Keep ? "保留" : "忽略"));
        }
        ItemPreferenceCountText.Text = $"共 {_itemPreferenceRows.Count} 项";
        RestoreItemPreferenceButton.IsEnabled = false;
    }

    private void ItemPreferencesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreItemPreferenceButton.IsEnabled = ItemPreferencesList.SelectedItem
            is ItemPreferenceRow;
    }

    private void RestoreItemPreference_Click(object sender, RoutedEventArgs e)
    {
        if (ItemPreferencesList.SelectedItem is ItemPreferenceRow row)
        {
            ApplyItemDisposition(
                row.Path,
                DesktopItemDisposition.Inbox,
                "项目处置偏好已移除；若项目仍存在，将重新进入收件箱。");
        }
    }

    private void LoadFavorites(Guid? selectedCollectionId = null)
    {
        var preferredInboxId = (InboxFavoriteCombo.SelectedItem as FavoriteCollectionRow)?.Id;
        var preferredListId = selectedCollectionId
            ?? (FavoritesList.SelectedItem as FavoriteCollectionRow)?.Id;
        _favoriteRows.Clear();
        foreach (var collection in _favoriteLibrary.Collections)
        {
            _favoriteRows.Add(new FavoriteCollectionRow(
                collection.Id,
                collection.Name,
                collection.ItemPaths.Length));
        }

        InboxFavoriteCombo.SelectedItem = _favoriteRows.FirstOrDefault(row => row.Id == preferredInboxId)
            ?? _favoriteRows.FirstOrDefault();
        FavoritesList.SelectedItem = _favoriteRows.FirstOrDefault(row => row.Id == preferredListId)
            ?? _favoriteRows.FirstOrDefault();
        RefreshFavoriteMembers();
    }

    private void InboxFavoriteCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddToFavoriteButton.IsEnabled = InboxFavoriteCombo.SelectedItem is FavoriteCollectionRow
            && GetSelectedPreviewRows().Length > 0;
    }

    private void AddToFavorite_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = GetSelectedPreviewRows();
        if (selectedRows.Length == 0
            || InboxFavoriteCombo.SelectedItem is not FavoriteCollectionRow collection)
        {
            return;
        }

        foreach (var item in selectedRows)
        {
            _favoriteLibrary = _favoriteLibrary.AddItem(collection.Id, item.SourcePath);
        }
        PersistFavorites(
            $"已将 {selectedRows.Length} 个项目加入收藏夹“{collection.Name}”；未移动文件。",
            collection.Id);
        StatusText.Text = $"已将 {selectedRows.Length} 个项目加入收藏夹“{collection.Name}”；未移动文件。";
    }

    private void CreateFavorite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _favoriteLibrary = _favoriteLibrary.AddCollection(FavoriteNameText.Text, out var created);
            FavoriteNameText.Clear();
            PersistFavorites($"已新建收藏夹“{created.Name}”。", created.Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            FavoritesStatusText.Text = exception.Message;
        }
    }

    private void RenameFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow selected)
        {
            return;
        }
        try
        {
            _favoriteLibrary = _favoriteLibrary.Rename(selected.Id, FavoriteNameText.Text);
            PersistFavorites("收藏夹已重命名。", selected.Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            FavoritesStatusText.Text = exception.Message;
        }
    }

    private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow selected)
        {
            return;
        }
        var confirmation = MessageBox.Show(
            $"删除收藏夹“{selected.Name}”？\n\n只会删除逻辑分组，不会删除或移动任何文件。",
            "删除收藏夹",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation is not MessageBoxResult.Yes)
        {
            return;
        }

        _favoriteLibrary = _favoriteLibrary.RemoveCollection(selected.Id);
        FavoriteNameText.Clear();
        PersistFavorites($"已删除收藏夹“{selected.Name}”；文件未改变。", null);
    }

    private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = FavoritesList.SelectedItem as FavoriteCollectionRow;
        RenameFavoriteButton.IsEnabled = selected is not null;
        DeleteFavoriteButton.IsEnabled = selected is not null;
        if (selected is not null)
        {
            FavoriteNameText.Text = selected.Name;
        }
        RefreshFavoriteMembers();
    }

    private void FavoriteMembersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = FavoriteMembersList.SelectedItem as FavoriteMemberRow;
        LocateFavoriteMemberButton.IsEnabled = selected is { Exists: true };
        RebindFavoriteMemberButton.IsEnabled = selected is { Exists: false };
        RemoveFavoriteMemberButton.IsEnabled = selected is not null;
    }

    private void RemoveFavoriteMember_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow collection
            || FavoriteMembersList.SelectedItem is not FavoriteMemberRow member)
        {
            return;
        }

        _favoriteLibrary = _favoriteLibrary.RemoveItem(collection.Id, member.Path);
        PersistFavorites($"已从“{collection.Name}”移除“{member.Name}”；文件未改变。", collection.Id);
    }

    private void LocateFavoriteMember_Click(object sender, RoutedEventArgs e)
    {
        if (FavoriteMembersList.SelectedItem is not FavoriteMemberRow { Exists: true } member)
        {
            return;
        }

        try
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            if (!File.Exists(explorerPath))
            {
                throw new FileNotFoundException("找不到 Windows 资源管理器。", explorerPath);
            }
            var startInfo = new ProcessStartInfo(explorerPath)
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(member.Path);
            _ = Process.Start(startInfo);
            FavoritesStatusText.Text = "已请求资源管理器定位选中成员；未打开或执行该项目。";
            _diagnosticLog.Write(
                DiagnosticLevel.Information,
                "Favorites",
                "已请求资源管理器定位一个收藏夹成员。");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            FavoritesStatusText.Text = $"无法启动资源管理器：{exception.Message}";
            _diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Favorites",
                "无法启动资源管理器定位收藏夹成员。",
                exception);
        }
    }

    private void RebindFavoriteMember_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow collection
            || FavoriteMembersList.SelectedItem is not FavoriteMemberRow { Exists: false } member)
        {
            return;
        }
        if (File.Exists(member.Path) || Directory.Exists(member.Path))
        {
            RefreshFavoriteMembers();
            FavoritesStatusText.Text = "该成员已重新可用，无需重新绑定。";
            return;
        }

        try
        {
            var candidates = LoadFavoriteRebindCandidates();
            if (candidates.Count == 0)
            {
                FavoritesStatusText.Text = "当前演示桌面和真实桌面中没有可用于重新绑定的项目。";
                return;
            }
            var dialog = new FavoriteRebindWindow(member.Path, candidates)
            {
                Owner = this
            };
            if (dialog.ShowDialog() is not true || dialog.SelectedPath is not { } selectedPath)
            {
                return;
            }
            if (!File.Exists(selectedPath) && !Directory.Exists(selectedPath))
            {
                FavoritesStatusText.Text = "所选项目已不存在，请刷新后重试。";
                return;
            }

            _favoriteLibrary = _favoriteLibrary.RebindItem(collection.Id, member.Path, selectedPath);
            PersistFavorites("失效成员已重新绑定；未移动或重命名任何文件。", collection.Id);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidOperationException)
        {
            FavoritesStatusText.Text = $"无法重新绑定：{exception.Message}";
            _diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Favorites",
                "收藏夹成员重新绑定失败。",
                exception);
        }
    }

    private void RemoveMissingFavoriteMembers_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow collection)
        {
            return;
        }
        var missingPaths = _favoriteMemberRows
            .Where(member => !member.Exists)
            .Select(member => member.Path)
            .ToArray();
        if (missingPaths.Length == 0)
        {
            return;
        }
        var confirmation = MessageBox.Show(
            $"从收藏夹“{collection.Name}”移除 {missingPaths.Length} 条失效成员关系？\n\n"
            + "不会删除、移动或修改任何文件。",
            "清理失效成员",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation is not MessageBoxResult.Yes)
        {
            return;
        }

        _favoriteLibrary = _favoriteLibrary.RemoveItems(collection.Id, missingPaths);
        PersistFavorites($"已清理 {missingPaths.Length} 条失效成员关系；文件未改变。", collection.Id);
    }

    private IReadOnlyList<FavoriteRebindCandidate> LoadFavoriteRebindCandidates()
    {
        var candidates = new List<FavoriteRebindCandidate>();
        AddCandidates(DemoSourceDirectory, "演示", candidates);
        AddCandidates(GetConfiguredMonitoredDirectory(), "真实桌面", candidates);
        return candidates
            .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddCandidates(
        string sourceDirectory,
        string scope,
        ICollection<FavoriteRebindCandidate> candidates)
    {
        var snapshot = new DirectoryDesktopCatalog(
            sourceDirectory,
            DesktopItemDispositionPolicy.Empty).GetSnapshot();
        foreach (var item in snapshot.Items)
        {
            candidates.Add(new FavoriteRebindCandidate(
                item.Path,
                Path.GetFileName(item.Path),
                ToDisplayKind(item.Kind),
                scope));
        }
    }

    private void RefreshFavoriteMembers()
    {
        _favoriteMemberRows.Clear();
        if (FavoritesList.SelectedItem is not FavoriteCollectionRow selected)
        {
            LocateFavoriteMemberButton.IsEnabled = false;
            RebindFavoriteMemberButton.IsEnabled = false;
            RemoveFavoriteMemberButton.IsEnabled = false;
            RemoveMissingFavoriteMembersButton.IsEnabled = false;
            return;
        }

        var collection = _favoriteLibrary.Get(selected.Id);
        foreach (var path in collection.ItemPaths)
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            _favoriteMemberRows.Add(new FavoriteMemberRow(
                path,
                Path.GetFileName(path),
                exists ? "可用" : "已失效",
                exists));
        }
        RemoveMissingFavoriteMembersButton.IsEnabled = _favoriteMemberRows.Any(member => !member.Exists);
        LocateFavoriteMemberButton.IsEnabled = false;
        RebindFavoriteMemberButton.IsEnabled = false;
        RemoveFavoriteMemberButton.IsEnabled = false;
    }

    private void PersistFavorites(string message, Guid? selectedCollectionId)
    {
        _savedSettings = _savedSettings with
        {
            Favorites = _favoriteLibrary.Collections.ToArray()
        };
        _settingsStore.Save(_savedSettings);
        LoadFavorites(selectedCollectionId);
        FavoritesStatusText.Text = message;
        _diagnosticLog.Write(
            DiagnosticLevel.Information,
            "Favorites",
            $"收藏夹配置已更新：{_favoriteLibrary.Collections.Count} 个收藏夹，"
            + $"{_favoriteLibrary.Collections.Sum(collection => collection.ItemPaths.Length)} 个成员关系。");
    }

    private void ApplyNotifications_Click(object sender, RoutedEventArgs e)
    {
        if (!TimeOnly.TryParseExact(
                QuietHoursStartText.Text.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var start)
            || !TimeOnly.TryParseExact(
                QuietHoursEndText.Text.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var end))
        {
            NotificationSettingsStatusText.Text = "时间格式无效，请使用 24 小时制 HH:mm，例如 22:00。";
            return;
        }

        if (QuietHoursCheck.IsChecked is true && start == end)
        {
            NotificationSettingsStatusText.Text = "免打扰开始与结束时间不能相同。";
            return;
        }

        CurrentNotificationPreferences = new NotificationPreferences(
            NotificationsEnabledCheck.IsChecked is true,
            QuietHoursCheck.IsChecked is true,
            start,
            end);
        _savedSettings = _savedSettings with
        {
            NotificationPreferences = CurrentNotificationPreferences
        };
        _settingsStore.Save(_savedSettings);
        NotificationSettingsStatusText.Text = CurrentNotificationPreferences.IsEnabled
            ? CurrentNotificationPreferences.QuietHoursEnabled
                ? $"通知已开启；{start:HH\\:mm}–{end:HH\\:mm} 期间免打扰。"
                : "通知已开启；免打扰已关闭。"
            : "通知已关闭。";
    }

    private void LoadNotificationPreferences()
    {
        CurrentNotificationPreferences = _savedSettings.NotificationPreferences
            ?? NotificationPreferences.Default;
        NotificationsEnabledCheck.IsChecked = CurrentNotificationPreferences.IsEnabled;
        QuietHoursCheck.IsChecked = CurrentNotificationPreferences.QuietHoursEnabled;
        QuietHoursStartText.Text = CurrentNotificationPreferences.QuietHoursStart.ToString(
            "HH:mm",
            CultureInfo.InvariantCulture);
        QuietHoursEndText.Text = CurrentNotificationPreferences.QuietHoursEnd.ToString(
            "HH:mm",
            CultureInfo.InvariantCulture);
        NotificationSettingsStatusText.Text = CurrentNotificationPreferences.IsEnabled
            ? "通知设置已加载。"
            : "通知当前已关闭。";
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出桌面收纳备份",
            Filter = "桌面收纳备份 (*.dmbak)|*.dmbak",
            DefaultExt = ".dmbak",
            AddExtension = true,
            FileName = $"桌面收纳备份-{DateTime.Now:yyyyMMdd-HHmm}.dmbak"
        };
        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        try
        {
            BackupStatusText.Text = "正在导出备份…";
            var history = await _operationHistory.ListAsync(int.MaxValue);
            var package = new BackupPackage(
                new BackupManifest(
                    BackupPackageFormat.CurrentVersion,
                    DateTimeOffset.UtcNow,
                    typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown"),
                new BackupSettings(
                    _savedSettings.ManagedDirectory,
                    _ruleRows.Select(row => row.Rule).ToArray(),
                    CurrentNotificationPreferences,
                    _dispositionPolicy.Preferences.ToArray(),
                    CurrentGlobalHotKeyBinding,
                    _favoriteLibrary.Collections.ToArray(),
                    _savedSettings.CollectionWindows ?? new CollectionWindowsPreferences()),
                history.ToArray());
            await new BackupPackageService().ExportAsync(dialog.FileName, package);
            BackupStatusText.Text = $"备份已导出：{package.Settings.Rules.Length} 条规则、{package.Settings.ItemPreferences.Length} 项偏好、{package.Operations.Length} 条历史。";
        }
        catch (Exception exception) when (IsExpectedBackupError(exception))
        {
            _diagnosticLog.Write(DiagnosticLevel.Error, "Backup", "备份导出失败。", exception);
            BackupStatusText.Text = $"备份导出失败：{exception.Message}";
        }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择桌面收纳备份",
            Filter = "桌面收纳备份 (*.dmbak)|*.dmbak",
            DefaultExt = ".dmbak",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        try
        {
            BackupStatusText.Text = "正在校验备份包和路径边界…";
            var package = await new BackupPackageService().ReadAsync(dialog.FileName);
            var importedManagedDirectoryIsSafe = TryValidateManagedDirectory(
                package.Settings.ManagedDirectory,
                out var managedDirectoryMessage);
            var effectiveRealManagedDirectory = importedManagedDirectoryIsSafe
                ? package.Settings.ManagedDirectory!
                : TryValidateManagedDirectory(_savedSettings.ManagedDirectory, out _)
                    ? _savedSettings.ManagedDirectory!
                    : Path.Combine(AppDataLocation.Root, "UnconfiguredManagedDirectory");
            var plan = BackupRestorePlanner.Create(
                package,
                DemoSourceDirectory,
                ManagedDirectory,
                GetConfiguredMonitoredDirectory(),
                effectiveRealManagedDirectory);
            var directorySummary = importedManagedDirectoryIsSafe
                ? $"将恢复托管目录：{package.Settings.ManagedDirectory}"
                : $"不会恢复备份中的托管目录：{managedDirectoryMessage}";
            var requestedHotKey = package.Settings.GlobalHotKeyBinding is null
                ? CurrentGlobalHotKeyBinding
                : plan.GlobalHotKeyBinding;
            var confirmation = MessageBox.Show(
                $"备份格式与内容校验通过。\n\n"
                + $"规则：{plan.Rules.Length} 条\n"
                + $"项目处置偏好：{plan.ItemPreferences.Length} 项（跳过 {plan.SkippedItemPreferenceCount} 项越界路径）\n"
                + $"操作历史：{plan.Operations.Length} 条（跳过 {plan.SkippedOperationCount} 条不兼容路径）\n"
                + $"收藏夹：{plan.Favorites.Length} 个（跳过 {plan.SkippedFavoriteMemberCount} 个越界成员）\n"
                + $"收纳窗口：{plan.CollectionWindows.EffectiveLayouts.Count} 个布局\n"
                + $"全局快捷键：{requestedHotKey.DisplayText}\n"
                + $"{directorySummary}\n\n"
                + "确认后将合并历史并替换规则、通知设置与项目偏好。",
                "确认恢复备份",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirmation is not MessageBoxResult.OK)
            {
                BackupStatusText.Text = "恢复已取消，当前数据未改变。";
                return;
            }

            foreach (var scoped in plan.Operations)
            {
                var journal = scoped.Scope is OperationScope.Demo
                    ? _demoOperationJournal
                    : _realOperationJournal;
                await journal.SaveAsync(scoped.Operation);
            }

            if (requestedHotKey != CurrentGlobalHotKeyBinding)
            {
                GlobalHotKeyChangeRequested?.Invoke(requestedHotKey);
            }
            var effectiveHotKey = CurrentGlobalHotKeyBinding;
            var hotKeySummary = effectiveHotKey == requestedHotKey
                ? $"快捷键已恢复为 {effectiveHotKey.DisplayText}。"
                : $"备份快捷键未能注册，已保留 {effectiveHotKey.DisplayText}。";

            StopWatching();
            _savedSettings = _savedSettings with
            {
                ManagedDirectory = importedManagedDirectoryIsSafe
                    ? package.Settings.ManagedDirectory
                    : _savedSettings.ManagedDirectory,
                Rules = plan.Rules,
                NotificationPreferences = plan.Notifications,
                ItemPreferences = plan.ItemPreferences,
                GlobalHotKeyBinding = effectiveHotKey,
                Favorites = plan.Favorites,
                CollectionWindows = plan.CollectionWindows
            };
            _settingsStore.Save(_savedSettings);
            LoadSettings();
            _currentPlan = null;
            ExecuteButton.IsEnabled = false;
            await LoadHistoryAsync();
            Scan(clearStatus: false);
            BackupStatusText.Text = $"恢复完成：已合并 {plan.Operations.Length} 条历史；{hotKeySummary}";
        }
        catch (Exception exception) when (IsExpectedBackupError(exception))
        {
            _diagnosticLog.Write(DiagnosticLevel.Error, "Backup", "备份导入失败。", exception);
            BackupStatusText.Text = $"备份导入失败，设置未应用：{exception.Message}";
        }
    }

    private static bool IsExpectedBackupError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or System.Text.Json.JsonException
            or SecurityException;

    private void LoadDiagnosticSummary()
    {
        var environment = CreateDiagnosticEnvironment();
        DiagnosticEnvironmentText.Text =
            $"应用 {environment.ApplicationVersion} · {environment.OperatingSystem} · "
            + $".NET {environment.RuntimeVersion} · {environment.ProcessArchitecture}";
        try
        {
            var entries = _diagnosticLog.ReadRecent(1000);
            var warningCount = entries.Count(entry => entry.Level is DiagnosticLevel.Warning);
            var errorCount = entries.Count(entry => entry.Level is DiagnosticLevel.Error);
            DiagnosticStatusText.Text = entries.Count == 0
                ? "当前没有诊断事件。"
                : $"最近记录 {entries.Count} 条：警告 {warningCount}，错误 {errorCount}；"
                    + $"最后事件 {entries[0].TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}。";
        }
        catch (Exception exception) when (IsExpectedDiagnosticError(exception))
        {
            DiagnosticStatusText.Text = $"无法读取诊断摘要：{exception.Message}";
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出桌面管理诊断包",
            Filter = "诊断包 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"DesktopManager-Diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        try
        {
            DiagnosticStatusText.Text = "正在生成脱敏诊断包…";
            var entries = _diagnosticLog.ReadRecent(2000);
            await new DiagnosticBundleService().ExportAsync(
                dialog.FileName,
                CreateDiagnosticEnvironment(),
                entries);
            _diagnosticLog.Write(
                DiagnosticLevel.Information,
                "Diagnostics",
                $"已导出诊断包，包含 {entries.Count} 条事件。");
            DiagnosticStatusText.Text = $"诊断包已导出，共 {entries.Count} 条脱敏事件。";
        }
        catch (Exception exception) when (IsExpectedDiagnosticError(exception))
        {
            _diagnosticLog.Write(DiagnosticLevel.Error, "Diagnostics", "诊断包导出失败。", exception);
            DiagnosticStatusText.Text = $"诊断包导出失败：{exception.Message}";
        }
    }

    private static DiagnosticEnvironment CreateDiagnosticEnvironment() => new(
        typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown",
        RuntimeInformation.OSDescription,
        Environment.Version.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        DateTimeOffset.UtcNow);

    private static bool IsExpectedDiagnosticError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or System.Text.Json.JsonException;

    private void ApplyStartup_Click(object sender, RoutedEventArgs e)
    {
        var enable = StartWithWindowsCheck.IsChecked is true;
        try
        {
            _startupRegistration.SetEnabled(enable);
            StartWithWindowsCheck.IsChecked = _startupRegistration.IsEnabled;
            StartupStatusText.Text = enable
                ? "已启用；下次登录 Windows 时将在系统托盘后台启动。"
                : "已关闭；登录 Windows 时不会自动启动。";
        }
        catch (Exception exception) when (IsExpectedStartupRegistrationError(exception))
        {
            _diagnosticLog.Write(DiagnosticLevel.Error, "Startup", "无法更新开机启动设置。", exception);
            StartupStatusText.Text = $"无法更新开机启动设置：{exception.Message}";
        }
    }

    private void LoadGlobalHotKeyPreference()
    {
        CurrentGlobalHotKeyBinding = GlobalHotKeyBinding.NormalizeOrDefault(
            _savedSettings.GlobalHotKeyBinding);
        HotKeyModifiersCombo.ItemsSource = new[]
        {
            "Ctrl + Alt",
            "Ctrl + Shift",
            "Ctrl + Win",
            "Alt + Shift",
            "Alt + Win",
            "Shift + Win",
            "Ctrl + Alt + Shift",
            "Ctrl + Alt + Win",
            "Ctrl + Shift + Win",
            "Alt + Shift + Win",
            "Ctrl + Alt + Shift + Win"
        };
        HotKeyKeyCombo.ItemsSource = new[] { "Space" }
            .Concat(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(value => value.ToString(CultureInfo.InvariantCulture)))
            .Concat(Enumerable.Range(1, 12).Select(value => $"F{value}"))
            .ToArray();
        SelectGlobalHotKey(CurrentGlobalHotKeyBinding);
        GlobalHotKeyStatusText.Text = $"当前快捷键：{CurrentGlobalHotKeyBinding.DisplayText}";
    }

    private void ApplyGlobalHotKey_Click(object sender, RoutedEventArgs e)
    {
        if (HotKeyModifiersCombo.SelectedItem is not string modifiers
            || HotKeyKeyCombo.SelectedItem is not string key)
        {
            GlobalHotKeyStatusText.Text = "请选择修饰键和按键。";
            return;
        }

        var ctrl = modifiers.Contains("Ctrl", StringComparison.Ordinal);
        var alt = modifiers.Contains("Alt", StringComparison.Ordinal);
        var shift = modifiers.Contains("Shift", StringComparison.Ordinal);
        var windows = modifiers.Contains("Win", StringComparison.Ordinal);
        if (!GlobalHotKeyBinding.TryCreate(
                key, ctrl, alt, shift, windows,
                out var binding, out var validationMessage))
        {
            GlobalHotKeyStatusText.Text = validationMessage;
            return;
        }
        if (GlobalHotKeyChangeRequested is null)
        {
            GlobalHotKeyStatusText.Text = "快捷键注册服务尚未就绪。";
            return;
        }

        GlobalHotKeyStatusText.Text = $"正在注册 {binding!.DisplayText}…";
        GlobalHotKeyChangeRequested.Invoke(binding);
    }

    public void CompleteGlobalHotKeyChange(
        GlobalHotKeyBinding requestedBinding,
        bool succeeded,
        string message)
    {
        if (succeeded)
        {
            CurrentGlobalHotKeyBinding = requestedBinding;
            _savedSettings = _savedSettings with
            {
                GlobalHotKeyBinding = requestedBinding
            };
            _settingsStore.Save(_savedSettings);
        }
        else
        {
            SelectGlobalHotKey(CurrentGlobalHotKeyBinding);
        }
        GlobalHotKeyStatusText.Text = message;
        _diagnosticLog.Write(
            succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
            "GlobalHotKey",
            message);
    }

    public void ReportGlobalHotKeyRegistrationFailure(string message)
    {
        GlobalHotKeyStatusText.Text = message;
        _diagnosticLog.Write(DiagnosticLevel.Warning, "GlobalHotKey", message);
    }

    private void SelectGlobalHotKey(GlobalHotKeyBinding binding)
    {
        var parts = binding.DisplayText.Split(" + ", StringSplitOptions.RemoveEmptyEntries);
        HotKeyKeyCombo.SelectedItem = parts[^1];
        HotKeyModifiersCombo.SelectedItem = string.Join(" + ", parts[..^1]);
    }

    private void LoadStartupRegistration()
    {
        try
        {
            var enabled = _startupRegistration.IsEnabled;
            StartWithWindowsCheck.IsChecked = enabled;
            StartupStatusText.Text = enabled
                ? "已启用；登录 Windows 后将在系统托盘后台启动。"
                : "当前未启用。";
        }
        catch (Exception exception) when (IsExpectedStartupRegistrationError(exception))
        {
            _diagnosticLog.Write(DiagnosticLevel.Error, "Startup", "无法读取开机启动设置。", exception);
            StartWithWindowsCheck.IsEnabled = false;
            ApplyStartupButton.IsEnabled = false;
            StartupStatusText.Text = $"无法读取开机启动设置：{exception.Message}";
        }
    }

    private static bool IsExpectedStartupRegistrationError(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException;

    private void UpdateSettingsValidation()
    {
        if (!TryValidateManagedDirectory(out var validationMessage))
        {
            SettingsValidationText.Text = validationMessage;
            SaveSettingsButton.IsEnabled = false;
            return;
        }

        SettingsValidationText.Text = "目录校验通过，可以保存。";
        SaveSettingsButton.IsEnabled = true;
    }

    private bool TryValidateManagedDirectory(out string message)
        => TryValidateDirectoryPair(
            MonitoredDirectoryText.Text,
            ManagedDirectoryText.Text,
            out message);

    private bool TryValidateManagedDirectory(string? managedDirectory, out string message)
        => TryValidateDirectoryPair(
            GetConfiguredMonitoredDirectory(),
            managedDirectory,
            out message);

    private bool TryValidateDirectoryPair(
        string? monitoredDirectory,
        string? managedDirectory,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(monitoredDirectory)
            || !Directory.Exists(monitoredDirectory))
        {
            message = "监控目录不存在，请重新选择。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(managedDirectory))
        {
            message = "尚未选择托管目录。";
            return false;
        }

        if (!Directory.Exists(managedDirectory))
        {
            message = "托管目录不存在，请重新选择。";
            return false;
        }

        try
        {
            _ = new FileOrganizer(
                _realOperationJournal,
                monitoredDirectory,
                managedDirectory);
            message = "目录校验通过。";
            return true;
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
            return false;
        }
    }

    private bool TryGetRealOperationContext(
        out string sourceDirectory,
        out string targetDirectory,
        out string message)
    {
        sourceDirectory = GetConfiguredMonitoredDirectory();
        targetDirectory = _savedSettings.ManagedDirectory ?? string.Empty;
        return TryValidateManagedDirectory(targetDirectory, out message);
    }

    private bool CanCreateRealDesktopPreview(out string message)
    {
        return TryValidateManagedDirectory(_savedSettings.ManagedDirectory, out message);
    }

    private void ConfigureRealDesktopPreviewAvailability()
    {
        var canPreview = CanCreateRealDesktopPreview(out var validationMessage);
        CreatePlanButton.IsEnabled = canPreview;
        ExecuteButton.IsEnabled = false;
        ManagedPathText.Text = canPreview
            ? $"预览归档位置：{_savedSettings.ManagedDirectory}"
            : $"预览归档位置：不可用（{validationMessage}）";
    }

    private string GetRealPreviewStatus(OrganizationPlan plan)
    {
        if (plan.Conflicts.Count > 0)
        {
            return $"已生成真实桌面预览：检测到 {plan.Conflicts.Count} 个规则冲突，当前不能执行。";
        }

        return $"已生成真实桌面预览：{plan.Items.Count} 项；执行预检通过后将直接整理。";
    }

    private void UpdateRealExecutionAvailabilityForCurrentPlan()
    {
        if (!_isRealDesktopMode || _currentPlan is null)
        {
            return;
        }

        ExecuteButton.IsEnabled = CanCreateRealDesktopPreview(out _)
            && _currentPlan.Conflicts.Count == 0;
    }

    private void LoadRules(OrganizationRule[]? rules)
    {
        _ruleRows.Clear();
        foreach (var rule in rules ?? CreateDefaultRules())
        {
            _ruleRows.Add(ToRuleRow(rule));
        }
    }

    private void RulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = RulesList.SelectedItem as RuleRow;
        ToggleRuleButton.IsEnabled = selected is not null;
        DeleteRuleButton.IsEnabled = selected is not null;
        EditRuleButton.IsEnabled = selected is not null;
        CopyRuleButton.IsEnabled = selected is not null;
        ToggleRuleButton.Content = selected?.Rule.IsEnabled is true ? "停用" : "启用";
        UpdateRuleImpactPreview(selected?.Rule);
    }

    private void UpdateRuleImpactPreview(OrganizationRule? rule)
    {
        if (rule is null || _snapshot is null)
        {
            RuleImpactText.Text = "选择一条规则可预览它对当前扫描快照的影响。";
            return;
        }

        var impact = OrganizationPlanner.PreviewRuleImpact(
            rule,
            _snapshot.Items,
            _snapshot.ObservedAt);
        var names = impact.MatchedItems
            .Take(8)
            .Select(item => Path.GetFileName(item.Path)
                + (item.IsReadOnly ? "（公共桌面只读）" : string.Empty));
        RuleImpactText.Text = impact.MatchedItems.Count == 0
            ? $"规则“{rule.Name}”当前未命中任何项目。"
            : $"规则“{rule.Name}”命中 {impact.MatchedItems.Count} 项"
                + (impact.ReadOnlyItemCount > 0
                    ? $"，其中 {impact.ReadOnlyItemCount} 项为公共桌面只读"
                    : string.Empty)
                + $"：{string.Join("、", names)}"
                + (impact.MatchedItems.Count > 8 ? " 等" : string.Empty);
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCreateRuleFromEditor(out var rule, out var message))
        {
            RulesStatusText.Text = message;
            return;
        }

        var editingIndex = _editingRuleId is { } editingRuleId
            ? _ruleRows.ToList().FindIndex(row => row.Rule.Id == editingRuleId)
            : -1;
        if (editingIndex >= 0)
        {
            _ruleRows[editingIndex] = ToRuleRow(rule!);
            ResetRuleEditor();
            PersistRules("规则修改已保存。");
            return;
        }

        _ruleRows.Add(ToRuleRow(rule!));
        ResetRuleEditor();
        PersistRules("规则已新增并保存。");
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow selected)
        {
            return;
        }

        _editingRuleId = selected.Rule.Id;
        LoadRuleIntoEditor(selected.Rule, selected.Rule.Name);
        RuleEditorTitle.Text = "编辑规则";
        SaveRuleButton.Content = "保存修改";
        CancelRuleEditButton.Visibility = Visibility.Visible;
        RulesStatusText.Text = "正在编辑规则；保存后当前整理计划会作废。";
    }

    private void EditRuleRow_Click(object sender, RoutedEventArgs e)
    {
        SelectRuleFromRowAction(sender);
        EditRule_Click(sender, e);
    }

    private void RuleActionsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            return;
        }
        RulesList.SelectedItem = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CopyRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow selected)
        {
            return;
        }

        _editingRuleId = null;
        LoadRuleIntoEditor(selected.Rule, selected.Rule.Name + " 副本");
        RuleEditorTitle.Text = "新增规则副本";
        SaveRuleButton.Content = "新增副本";
        CancelRuleEditButton.Visibility = Visibility.Visible;
        RulesStatusText.Text = "已复制到编辑器；调整后保存为新规则。";
    }

    private void CopyRuleRow_Click(object sender, RoutedEventArgs e)
    {
        SelectRuleFromRowAction(sender);
        CopyRule_Click(sender, e);
    }

    private void CancelRuleEdit_Click(object sender, RoutedEventArgs e)
    {
        ResetRuleEditor();
        RulesStatusText.Text = "已取消编辑。";
    }

    private void ToggleRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow selected)
        {
            return;
        }

        var index = _ruleRows.IndexOf(selected);
        var updated = selected.Rule with { IsEnabled = !selected.Rule.IsEnabled };
        _ruleRows[index] = ToRuleRow(updated);
        RulesList.SelectedIndex = index;
        PersistRules(updated.IsEnabled ? "规则已启用。" : "规则已停用并保留配置。");
    }

    private void ToggleRuleRow_Click(object sender, RoutedEventArgs e)
    {
        SelectRuleFromRowAction(sender);
        ToggleRule_Click(sender, e);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow selected)
        {
            return;
        }

        _ruleRows.Remove(selected);
        if (_editingRuleId == selected.Rule.Id)
        {
            ResetRuleEditor();
        }
        PersistRules("规则已删除。");
    }

    private void DeleteRuleRow_Click(object sender, RoutedEventArgs e)
    {
        SelectRuleFromRowAction(sender);
        DeleteRule_Click(sender, e);
    }

    private void SelectRuleFromRowAction(object sender)
    {
        if (sender is FrameworkElement { DataContext: RuleRow row })
        {
            RulesList.SelectedItem = row;
        }
    }

    private void RestoreDefaultRules_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "这会替换当前全部规则，是否恢复三条默认扩展名规则？",
            "恢复默认规则",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result is not MessageBoxResult.Yes)
        {
            return;
        }

        LoadRules(CreateDefaultRules());
        ResetRuleEditor();
        PersistRules("已恢复并保存默认规则。");
    }

    private bool TryCreateRuleFromEditor(out OrganizationRule? rule, out string message)
    {
        rule = null;
        var name = RuleNameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            message = "请输入规则名称。";
            return false;
        }

        if (_ruleRows.Any(row =>
                row.Rule.Id != _editingRuleId
                && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            message = "已存在同名规则。";
            return false;
        }

        var extensions = RuleExtensionsText.Text
            .Split([',', ';', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
            .Select(extension => extension.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (extensions.Any(extension => extension.IndexOfAny(['\\', '/', '*', '?']) >= 0))
        {
            message = "请输入有效扩展名，例如 .pdf,.docx。";
            return false;
        }

        var keywords = RuleKeywordsText.Text
            .Split([',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!TryParseOptionalMegabytes(
                RuleMinimumSizeText.Text,
                "最小大小",
                out var minimumSizeBytes,
                out message)
            || !TryParseOptionalMegabytes(
                RuleMaximumSizeText.Text,
                "最大大小",
                out var maximumSizeBytes,
                out message))
        {
            return false;
        }

        if (minimumSizeBytes is { } minimum
            && maximumSizeBytes is { } maximum
            && minimum > maximum)
        {
            message = "最小大小不能大于最大大小。";
            return false;
        }

        int? modifiedWithinDays = null;
        if (!string.IsNullOrWhiteSpace(RuleModifiedWithinDaysText.Text))
        {
            if (!int.TryParse(RuleModifiedWithinDaysText.Text, out var days) || days <= 0)
            {
                message = "最近修改天数必须是正整数。";
                return false;
            }
            modifiedWithinDays = days;
        }

        IReadOnlyList<DesktopItemKind> itemKinds = RuleItemKindCombo.SelectedIndex switch
        {
            1 => [DesktopItemKind.File],
            2 => [DesktopItemKind.Folder],
            3 => [DesktopItemKind.Shortcut],
            _ => []
        };
        var destination = RuleDestinationText.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination)
            || Path.IsPathRooted(destination)
            || destination.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
        {
            message = "归档子目录必须是托管目录内的相对路径。";
            return false;
        }

        if (!int.TryParse(RulePriorityText.Text, out var priority))
        {
            message = "优先级必须是整数。";
            return false;
        }

        var existingRule = _editingRuleId is { } editingRuleId
            ? _ruleRows.FirstOrDefault(row => row.Rule.Id == editingRuleId)?.Rule
            : null;
        rule = new OrganizationRule(
            existingRule?.Id ?? Guid.NewGuid(),
            name,
            priority,
            extensions,
            destination,
            IsEnabled: existingRule?.IsEnabled ?? true,
            FileNameKeywords: keywords,
            MinimumSizeBytes: minimumSizeBytes,
            MaximumSizeBytes: maximumSizeBytes,
            ItemKinds: itemKinds,
            ModifiedWithinDays: modifiedWithinDays);
        message = "规则有效。";
        return true;
    }

    private void LoadRuleIntoEditor(OrganizationRule rule, string name)
    {
        RuleNameText.Text = name;
        RuleExtensionsText.Text = string.Join(",", rule.Extensions);
        RuleKeywordsText.Text = string.Join(",", rule.FileNameKeywords ?? []);
        RuleMinimumSizeText.Text = rule.MinimumSizeBytes is { } minimum
            ? (minimum / (1024m * 1024m)).ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
        RuleMaximumSizeText.Text = rule.MaximumSizeBytes is { } maximum
            ? (maximum / (1024m * 1024m)).ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
        RuleModifiedWithinDaysText.Text = rule.ModifiedWithinDays?.ToString(CultureInfo.CurrentCulture)
            ?? string.Empty;
        RuleItemKindCombo.SelectedIndex = rule.ItemKinds is { Count: 1 }
            ? rule.ItemKinds[0] switch
            {
                DesktopItemKind.File => 1,
                DesktopItemKind.Folder => 2,
                DesktopItemKind.Shortcut => 3,
                _ => 0
            }
            : 0;
        RuleDestinationText.Text = rule.RelativeDestination;
        RulePriorityText.Text = rule.Priority.ToString(CultureInfo.CurrentCulture);
    }

    private void ResetRuleEditor()
    {
        _editingRuleId = null;
        RuleNameText.Clear();
        RuleExtensionsText.Clear();
        RuleKeywordsText.Clear();
        RuleMinimumSizeText.Clear();
        RuleMaximumSizeText.Clear();
        RuleModifiedWithinDaysText.Clear();
        RuleItemKindCombo.SelectedIndex = 0;
        RuleDestinationText.Clear();
        RulePriorityText.Text = "100";
        RuleEditorTitle.Text = "新增规则";
        SaveRuleButton.Content = "新增";
        CancelRuleEditButton.Visibility = Visibility.Collapsed;
    }

    private static bool TryParseOptionalMegabytes(
        string text,
        string fieldName,
        out long? bytes,
        out string message)
    {
        bytes = null;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var megabytes)
            || megabytes < 0
            || megabytes > long.MaxValue / (1024m * 1024m))
        {
            message = $"{fieldName}必须是有效的非负 MB 数值。";
            return false;
        }

        bytes = (long)(megabytes * 1024m * 1024m);
        return true;
    }

    private void PersistRules(string message)
    {
        _savedSettings = _savedSettings with
        {
            Rules = _ruleRows.Select(row => row.Rule).ToArray()
        };
        _settingsStore.Save(_savedSettings);
        _currentPlan = null;
        ExecuteButton.IsEnabled = false;
        PlanList.SelectedItem = null;
        ResolveConflictButton.IsEnabled = false;
        PlanCount.Text = "0";
        PlanSizeText.Text = "0 B";
        RiskCount.Text = "0 / 0";
        PlanSummaryText.Text = "当前整理计划已作废。";
        TargetDistributionText.Text = string.Empty;
        PlanRiskDetailsText.Text = string.Empty;
        RulesStatusText.Text = message + " 当前整理计划已作废，请重新生成。";
        SynchronizeCollectionWindows();
    }

    private static RuleRow ToRuleRow(OrganizationRule rule) => new(
        rule,
        rule.IsEnabled ? "启用" : "停用",
        rule.Name,
        DescribeConditions(rule),
        rule.RelativeDestination,
        rule.Priority.ToString());

    private static string DescribeConditions(OrganizationRule rule)
    {
        var parts = new List<string>();
        if (rule.Extensions.Count > 0)
        {
            parts.Add("扩展名 " + string.Join(",", rule.Extensions));
        }
        if (rule.FileNameKeywords is { Count: > 0 })
        {
            parts.Add("名称含 " + string.Join("/", rule.FileNameKeywords));
        }
        if (rule.ItemKinds is { Count: > 0 })
        {
            parts.Add("类型 " + string.Join("/", rule.ItemKinds.Select(ToDisplayKind)));
        }
        if (rule.MinimumSizeBytes is { } minimumSize)
        {
            parts.Add($"≥ {minimumSize / (1024m * 1024m):0.##} MB");
        }
        if (rule.MaximumSizeBytes is { } maximumSize)
        {
            parts.Add($"≤ {maximumSize / (1024m * 1024m):0.##} MB");
        }
        if (rule.ModifiedWithinDays is { } days)
        {
            parts.Add($"最近 {days} 天");
        }
        return parts.Count == 0 ? "全部项目" : string.Join("；", parts);
    }

    private static OrganizationRule[] CreateDefaultRules() =>
    [
        new OrganizationRule(Guid.NewGuid(), "文档归档", 100, [".txt"], "工作文档"),
        new OrganizationRule(Guid.NewGuid(), "截图归档", 100, [".png", ".jpg"], Path.Combine("图片", "截图")),
        new OrganizationRule(Guid.NewGuid(), "压缩包归档", 100, [".zip", ".7z"], "压缩包")
    ];

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e) =>
        await LoadHistoryAsync();

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = HistoryList.SelectedItem as HistoryRow;
        _historyItemRows.Clear();
        if (selected is not null)
        {
            foreach (var item in selected.Operation.Items)
            {
                var canUndoItem = selected.Operation.Kind is OperationKind.Organize
                    && item.Status is OperationItemStatus.Succeeded
                    && !selected.RestoredTargetPaths.Contains(Path.GetFullPath(item.TargetPath));
                _historyItemRows.Add(new HistoryItemRow(
                    item.SourcePath,
                    item.TargetPath,
                    ToDisplayStatus(item.Status),
                    item.Error ?? "—",
                    item.TargetPath,
                    canUndoItem));
            }
        }

        var canUndoHere = selected is not null && CanUndoHistoryRow(selected);
        UndoSelectedButton.IsEnabled = canUndoHere;
        UndoSelectedItemButton.IsEnabled = false;
        HistoryStatusText.Text = selected is null
            ? "选择一条记录查看可用操作。"
            : canUndoHere
                ? "该操作可以撤销；撤销前会再次验证允许的路径范围。"
                : selected.CanUndo && selected.Scope is OperationScope.RealDesktop
                    ? "真实撤销需要进入真实桌面模式，并保持当前目录设置有效。"
                    : "该操作当前不可撤销。";
    }

    private async void UndoSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow selected && CanUndoHistoryRow(selected))
        {
            var request = CreateBatchUndoRequest(selected);
            if (request is not null)
            {
                await UndoOperationAsync(selected, request);
            }
        }
    }

    private UndoRequest? CreateBatchUndoRequest(HistoryRow selected)
    {
        var remaining = selected.Operation.Items
            .Where(item => item.Status is OperationItemStatus.Succeeded)
            .Where(item => !selected.RestoredTargetPaths.Contains(Path.GetFullPath(item.TargetPath)))
            .ToArray();
        if (!remaining.Any(item => Path.Exists(item.SourcePath)))
        {
            return new UndoRequest();
        }

        var choice = MessageBox.Show(
            "部分项目的原位置已有同名项目。\n\n选择“是”将使用安全名称恢复；选择“否”将跳过冲突项；选择“取消”停止撤销。",
            "处理撤销位置冲突",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return choice switch
        {
            MessageBoxResult.Yes => new UndoRequest(
                ConflictResolution: UndoConflictResolution.SafeRename),
            MessageBoxResult.No => new UndoRequest(
                ConflictResolution: UndoConflictResolution.Skip),
            _ => null
        };
    }

    private void HistoryItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UndoSelectedItemButton.IsEnabled = HistoryList.SelectedItem is HistoryRow selected
            && CanUndoHistoryRow(selected)
            && HistoryItemList.SelectedItem is HistoryItemRow { CanUndo: true };
    }

    private async void UndoSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryRow selected
            || HistoryItemList.SelectedItem is not HistoryItemRow { CanUndo: true } item
            || !CanUndoHistoryRow(selected))
        {
            return;
        }

        var resolution = UndoConflictResolution.Fail;
        string? alternatePath = null;
        var originalItem = selected.Operation.Items.First(candidate =>
            string.Equals(candidate.TargetPath, item.OriginalTargetPath, StringComparison.OrdinalIgnoreCase));
        if (Path.Exists(originalItem.SourcePath))
        {
            var sourceRoot = selected.Scope is OperationScope.RealDesktop
                ? _activeSourceDirectory
                : DemoSourceDirectory;
            var dialog = new UndoConflictWindow(originalItem.SourcePath, sourceRoot) { Owner = this };
            if (dialog.ShowDialog() is not true)
            {
                return;
            }
            resolution = dialog.Resolution;
            alternatePath = dialog.AlternateRestorePath;
        }

        await UndoOperationAsync(
            selected,
            new UndoRequest([item.OriginalTargetPath], resolution, alternatePath));
    }

    private async Task LoadHistoryAsync()
    {
        var completeHistory = await _operationHistory.ListAsync(5_000);
        var operations = completeHistory.Take(50).ToArray();
        var restoredByOperation = completeHistory
            .Where(item => item.Operation.Kind is OperationKind.Undo
                && item.Operation.ReversesOperationId is not null)
            .GroupBy(item => (item.Scope, OriginalId: item.Operation.ReversesOperationId!.Value))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group
                    .SelectMany(item => item.Operation.Items)
                    .Where(item => item.Status is OperationItemStatus.Succeeded)
                    .Select(item => Path.GetFullPath(item.SourcePath))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
        _historyRows.Clear();
        _historyItemRows.Clear();
        foreach (var operation in operations)
        {
            restoredByOperation.TryGetValue(
                (operation.Scope, operation.Operation.Id),
                out var restoredTargets);
            _historyRows.Add(ToHistoryRow(operation, restoredTargets ?? new HashSet<string>()));
        }

        HistoryCount.Text = operations.Length.ToString();
        RecoverableCount.Text = _historyRows.Count(row => row.CanUndo).ToString();
        UpdateActiveUndoAvailability();
        UndoSelectedButton.IsEnabled = false;
        UndoSelectedItemButton.IsEnabled = false;
        HistoryStatusText.Text = operations.Length == 0
            ? "还没有整理操作记录。"
            : "已合并加载演示与真实桌面的独立 SQLite 操作记录。";
    }

    private async Task UndoOperationAsync(HistoryRow historyRow, UndoRequest request)
    {
        var journal = _demoOperationJournal;
        var sourceDirectory = DemoSourceDirectory;
        var targetDirectory = ManagedDirectory;
        if (historyRow.Scope is OperationScope.RealDesktop)
        {
            if (!TryGetRealOperationContext(
                    out sourceDirectory,
                    out targetDirectory,
                    out var contextMessage))
            {
                StatusText.Text = contextMessage;
                HistoryStatusText.Text = contextMessage;
                return;
            }

            journal = _realOperationJournal;
        }

        UndoButton.IsEnabled = false;
        UndoSelectedButton.IsEnabled = false;
        var operation = await new FileOrganizer(
            journal,
            sourceDirectory,
            targetDirectory).UndoAsync(historyRow.OperationId, request);
        var message = operation.Status is OperationStatus.Completed
            ? $"撤销完成：文件已回到{(historyRow.Scope is OperationScope.Demo ? "演示目录" : "真实桌面")}原位置。"
            : "撤销部分完成，请检查操作结果。";
        StatusText.Text = message;
        HistoryStatusText.Text = message;
        NotificationRequested?.Invoke("桌面整理撤销完成", message);
        _diagnosticLog.Write(
            operation.Status is OperationStatus.Completed ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
            "Undo",
            $"{historyRow.Scope} 撤销结束：状态 {operation.Status}，项目 {operation.Items.Length}。");
        Scan(clearStatus: false);
        await LoadHistoryAsync();
    }

    private bool CanUndoHistoryRow(HistoryRow historyRow)
    {
        if (!historyRow.CanUndo)
        {
            return false;
        }

        if (historyRow.Scope is OperationScope.Demo)
        {
            return !_isRealDesktopMode;
        }

        return _isRealDesktopMode
            && TryGetRealOperationContext(out _, out _, out _);
    }

    private void UpdateActiveUndoAvailability()
    {
        var activeScope = _isRealDesktopMode
            ? OperationScope.RealDesktop
            : OperationScope.Demo;
        _lastRecoverableOperation = _historyRows.FirstOrDefault(row =>
            row.CanUndo && row.Scope == activeScope);
        UndoButton.IsEnabled = _lastRecoverableOperation is not null
            && CanUndoHistoryRow(_lastRecoverableOperation);
    }

    private static HistoryRow ToHistoryRow(
        ScopedOrganizationOperation scopedOperation,
        IReadOnlySet<string> restoredTargetPaths)
    {
        var operation = scopedOperation.Operation;
        var succeeded = operation.Items.Count(item => item.Status is OperationItemStatus.Succeeded);
        var failed = operation.Items.Count(item => item.Status is OperationItemStatus.Failed);
        var remainingUndoCount = operation.Items.Count(item =>
            item.Status is OperationItemStatus.Succeeded
            && !restoredTargetPaths.Contains(Path.GetFullPath(item.TargetPath)));
        var canUndo = operation.Kind is OperationKind.Organize
            && remainingUndoCount > 0
            && operation.Status is OperationStatus.Completed or OperationStatus.PartiallyCompleted;
        return new HistoryRow(
            operation.Id,
            scopedOperation.Scope,
            (scopedOperation.Scope is OperationScope.Demo ? "演示" : "真实桌面")
                + (operation.Kind is OperationKind.Undo ? "·撤销" : "·整理"),
            operation.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            ToDisplayStatus(operation.Status),
            operation.Kind is OperationKind.Undo
                ? $"{operation.Items.Length} 项 · 恢复 {succeeded} · 失败 {failed}"
                : $"{operation.Items.Length} 项 · 成功 {succeeded} · 待撤销 {remainingUndoCount}",
            operation.Id.ToString("N"),
            canUndo,
            operation,
            restoredTargetPaths);
    }

    private static string ToDisplayStatus(OperationStatus status) => status switch
    {
        OperationStatus.Running => "执行中",
        OperationStatus.PartiallyCompleted => "部分完成",
        OperationStatus.Completed => "已完成",
        OperationStatus.Failed => "失败",
        OperationStatus.Undone => "已撤销",
        _ => "未知"
    };

    private static string ToDisplayStatus(OperationItemStatus status) => status switch
    {
        OperationItemStatus.Pending => "待处理",
        OperationItemStatus.Succeeded => "成功",
        OperationItemStatus.Skipped => "已跳过",
        OperationItemStatus.Failed => "失败",
        OperationItemStatus.Undone => "已撤销",
        _ => "未知"
    };

    private void StartWatchingRealDesktop()
    {
        var batcher = new DesktopChangeBatcher(TimeSpan.FromMilliseconds(300), changes =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.InvokeAsync(() => RefreshAfterDesktopChanges(changes));
            }
        });
        _changeBatcher = batcher;
        _watchSubscription = CreateRealDesktopCatalog().ObserveChanges(batcher.Signal);
    }

    private DesktopSnapshot GetActiveSnapshot() => _isRealDesktopMode
        ? CreateRealDesktopCatalog().GetSnapshot()
        : new DirectoryDesktopCatalog(_activeSourceDirectory, _dispositionPolicy).GetSnapshot();

    private CombinedDesktopCatalog CreateRealDesktopCatalog()
    {
        string? publicDesktop = null;
        if (_savedSettings.IncludePublicDesktopReadOnly)
        {
            try
            {
                var candidate = WindowsDesktopLocation.GetPublicDesktop();
                publicDesktop = Directory.Exists(candidate) ? candidate : null;
            }
            catch (DirectoryNotFoundException)
            {
                publicDesktop = null;
            }
        }
        return new CombinedDesktopCatalog(
            _activeSourceDirectory,
            _dispositionPolicy,
            publicDesktop);
    }

    private string GetConfiguredMonitoredDirectory() =>
        !string.IsNullOrWhiteSpace(_savedSettings.MonitoredDirectory)
            ? Path.GetFullPath(_savedSettings.MonitoredDirectory)
            : WindowsDesktopLocation.GetCurrentUserDesktop();

    private void StopWatching()
    {
        _watchSubscription?.Dispose();
        _watchSubscription = null;
        _changeBatcher?.Dispose();
        _changeBatcher = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        RestoreAccessibilityTheme();
        StopWatching();
        _collectionWindows.Dispose();
        _desktopWidgets.Dispose();
        base.OnClosed(e);
    }

    private sealed record PreviewRow(
        Guid DesktopItemId,
        string SourcePath,
        string Name,
        string Kind,
        DesktopItemKind KindValue,
        long Size,
        DateTimeOffset ModifiedAt,
        string Action,
        string Target,
        string Explanation,
        bool IsConflict)
    {
        public string SizeText => KindValue is DesktopItemKind.Folder
            ? "—"
            : Size switch
            {
                < 1024 => $"{Size} B",
                < 1024 * 1024 => $"{Size / 1024d:0.#} KB",
                < 1024L * 1024 * 1024 => $"{Size / (1024d * 1024):0.#} MB",
                _ => $"{Size / (1024d * 1024 * 1024):0.#} GB"
            };

        public string ModifiedText => ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private sealed record HistoryRow(
        Guid OperationId,
        OperationScope Scope,
        string ScopeText,
        string StartedAt,
        string Status,
        string ItemSummary,
        string OperationIdText,
        bool CanUndo,
        OrganizationOperation Operation,
        IReadOnlySet<string> RestoredTargetPaths);

    private sealed record HistoryItemRow(
        string Source,
        string Target,
        string Status,
        string Error,
        string OriginalTargetPath,
        bool CanUndo);

    private sealed record ItemPreferenceRow(string Path, string Disposition);

    private sealed record FavoriteCollectionRow(Guid Id, string Name, int Count);

    private sealed record FavoriteMemberRow(string Path, string Name, string Status, bool Exists);

    private sealed record CollectionWindowRow(
        Guid ZoneId,
        string Status,
        string Name,
        string RelativeDirectory,
        string RuleCountText,
        string RuleStatus,
        bool IsVisible);

    private sealed record RuleRow(
        OrganizationRule Rule,
        string Status,
        string Name,
        string Conditions,
        string Destination,
        string Priority);
}
