using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopManager.App;

internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _openInboxItem;
    private readonly Drawing.Icon? _applicationIcon;
    private bool _hiddenNotificationShown;

    public TrayIconController(
        Action openInbox,
        Action showAllCollectionWindows,
        Action hideAllCollectionWindows,
        Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(openInbox);
        ArgumentNullException.ThrowIfNull(showAllCollectionWindows);
        ArgumentNullException.ThrowIfNull(hideAllCollectionWindows);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _openInboxItem = new Forms.ToolStripMenuItem("打开收件箱");
        _openInboxItem.Click += (_, _) => openInbox();
        var showAllItem = new Forms.ToolStripMenuItem("显示所有收纳窗口");
        showAllItem.Click += (_, _) => showAllCollectionWindows();
        var hideAllItem = new Forms.ToolStripMenuItem("隐藏所有收纳窗口");
        hideAllItem.Click += (_, _) => hideAllCollectionWindows();
        var exitItem = new Forms.ToolStripMenuItem("退出应用");
        exitItem.Click += (_, _) => exitApplication();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_openInboxItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(showAllItem);
        menu.Items.Add(hideAllItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _applicationIcon = TryLoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? Drawing.SystemIcons.Application,
            Text = "桌面管理 · 待整理 0 项",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => openInbox();
    }

    public void UpdatePendingCount(int count)
    {
        var safeCount = Math.Max(0, count);
        _notifyIcon.Text = $"桌面管理 · 待整理 {safeCount} 项";
        _openInboxItem.Text = $"打开收件箱（{safeCount}）";
    }

    public void ShowHiddenNotificationOnce()
    {
        if (_hiddenNotificationShown)
        {
            return;
        }

        _hiddenNotificationShown = true;
        _notifyIcon.BalloonTipTitle = "桌面管理仍在运行";
        _notifyIcon.BalloonTipText = "双击托盘图标可重新打开收件箱。";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2000);
    }

    public void ShowInformation(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void ShowWarning(string message)
    {
        _notifyIcon.BalloonTipTitle = "桌面管理";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
    }

    private static Drawing.Icon? TryLoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            return Drawing.Icon.ExtractAssociatedIcon(executablePath);
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            return null;
        }
    }
}
