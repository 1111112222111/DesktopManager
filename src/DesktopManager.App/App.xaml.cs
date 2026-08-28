using System.ComponentModel;
using System.IO;
using System.Windows;
using DesktopManager.Core;
using DesktopManager.Infrastructure;

namespace DesktopManager.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private readonly WindowLifetimeController _windowLifetime = new();
    private TrayIconController? _trayIcon;
    private GlobalHotKeyController? _globalHotKey;
    private IDiagnosticLog? _diagnosticLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _diagnosticLog = new FileDiagnosticLog(Path.Combine(AppDataLocation.Root, "Logs"));
        DispatcherUnhandledException += (_, args) => _diagnosticLog.Write(
            DiagnosticLevel.Error,
            "Application",
            "发生未处理的界面线程异常，应用将按默认策略处理。",
            args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _diagnosticLog.Write(
            DiagnosticLevel.Error,
            "Application",
            "发生未处理的进程异常。",
            args.ExceptionObject as Exception);
        _diagnosticLog.Write(DiagnosticLevel.Information, "Application", "应用启动。");
        _singleInstance = new SingleInstanceCoordinator(
            AppDataLocation.IsOverridden ? "DesktopManager.App.Isolated" : "DesktopManager.App");
        if (!_singleInstance.TryAcquire(ActivateMainWindow))
        {
            _diagnosticLog.Write(DiagnosticLevel.Information, "Application", "检测到已有实例，已请求激活。");
            Shutdown(0);
            return;
        }

        var window = new MainWindow(_diagnosticLog);
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        if (AppDataLocation.IsOverridden
            && e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            window.Loaded += (_, _) => _ = Dispatcher.BeginInvoke(() =>
            {
                if (!window.AreCollectionWindowsDesktopHosted)
                {
                    throw new InvalidOperationException("收纳窗口未全部挂接到 Windows 桌面宿主层。");
                }
                _windowLifetime.RequestExit();
                window.Close();
            });
        }
        _trayIcon = new TrayIconController(
            OpenInboxFromTray,
            ShowAllCollectionWindowsFromTray,
            HideAllCollectionWindowsFromTray,
            RequestExit);
        var configuredHotKey = GlobalHotKeyBinding.NormalizeOrDefault(
            new JsonAppSettingsStore(DesktopManager.App.MainWindow.SettingsFilePath)
                .Load()
                .GlobalHotKeyBinding);
        _globalHotKey = new GlobalHotKeyController(
            window,
            configuredHotKey,
            OpenInboxFromTray,
            message =>
            {
                _trayIcon.ShowWarning(message);
                if (_globalHotKey is { IsRegistered: true } activeHotKey
                    && activeHotKey.CurrentBinding != configuredHotKey)
                {
                    window.CompleteGlobalHotKeyChange(
                        activeHotKey.CurrentBinding,
                        succeeded: true,
                        message);
                }
                else
                {
                    window.ReportGlobalHotKeyRegistrationFailure(message);
                }
            });
        window.GlobalHotKeyChangeRequested += binding =>
        {
            var succeeded = _globalHotKey.TryChangeBinding(binding, out var message);
            window.CompleteGlobalHotKeyChange(binding, succeeded, message);
            if (!succeeded)
            {
                _trayIcon.ShowWarning(message);
            }
        };
        window.InboxCountChanged += count => _trayIcon.UpdatePendingCount(count);
        window.NotificationRequested += ShowNotificationWhenAppropriate;
        SessionEnding += (_, _) => _windowLifetime.RequestExit();
        if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            window.Hide();
            window.ShowInTaskbar = true;
            window.ShowActivated = true;
        }
        else
        {
            window.Show();
        }
    }

    private void ActivateMainWindow()
    {
        _ = Dispatcher.BeginInvoke(() => RestoreMainWindow(openInbox: false));
    }

    private void OpenInboxFromTray()
    {
        _ = Dispatcher.BeginInvoke(() => RestoreMainWindow(openInbox: true));
    }

    private void ShowAllCollectionWindowsFromTray() =>
        _ = Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowAllCollectionWindows());

    private void HideAllCollectionWindowsFromTray() =>
        _ = Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.HideAllCollectionWindows());

    private void RestoreMainWindow(bool openInbox)
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        if (openInbox && window is MainWindow mainWindow)
        {
            mainWindow.OpenInbox();
        }

        if (window.WindowState is WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_windowLifetime.HandleClose() is WindowCloseAction.ExitApplication)
        {
            return;
        }

        e.Cancel = true;
        MainWindow?.Hide();
        if (MainWindow is MainWindow window && CanShowNotification(window))
        {
            _trayIcon?.ShowHiddenNotificationOnce();
        }
    }

    private void ShowNotificationWhenAppropriate(string title, string message)
    {
        if (MainWindow is not MainWindow window || window.IsVisible)
        {
            return;
        }

        if (CanShowNotification(window))
        {
            _trayIcon?.ShowInformation(title, message);
        }
    }

    private static bool CanShowNotification(MainWindow window) =>
        NotificationPolicy.Evaluate(
            window.CurrentNotificationPreferences,
            TimeOnly.FromDateTime(DateTime.Now)) is NotificationDecision.Show;

    private void RequestExit()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            _windowLifetime.RequestExit();
            MainWindow?.Close();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnosticLog?.Write(DiagnosticLevel.Information, "Application", $"应用退出，代码 {e.ApplicationExitCode}。");
        _globalHotKey?.Dispose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
