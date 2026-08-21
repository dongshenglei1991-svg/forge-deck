using System.Drawing;
using WinForms = System.Windows.Forms;

namespace ForgeDeck.App;

/// <summary>仅在窗口因关闭行为隐藏时占用托盘。不引进 Core。</summary>
internal sealed class TrayIconHost : IDisposable
{
    private WinForms.NotifyIcon? _icon;
    private bool _balloonShown;

    public event Action? RestoreRequested;
    public event Action? ExitRequested;

    public bool Visible => _icon != null;

    public bool Show()
    {
        if (_icon != null) return true;
        try
        {
            var notify = new WinForms.NotifyIcon
            {
                Text = "ForgeDeck",
                Icon = TryLoadIcon() ?? SystemIcons.Application,
                Visible = true,
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, _) => RestoreRequested?.Invoke());
            menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
            notify.ContextMenuStrip = menu;
            notify.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                    RestoreRequested?.Invoke();
            };
            _icon = notify;
            if (!_balloonShown)
            {
                _balloonShown = true;
                notify.BalloonTipTitle = "ForgeDeck";
                notify.BalloonTipText = "ForgeDeck 仍在运行。点击托盘图标可恢复。";
                notify.ShowBalloonTip(4000);
            }
            return true;
        }
        catch
        {
            Hide();
            return false;
        }
    }

    public void Hide()
    {
        if (_icon == null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }

    public void Dispose() => Hide();

    private static Icon? TryLoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            return null;
        }
    }
}
