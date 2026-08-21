using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinForms = System.Windows.Forms;

namespace ForgeDeck.App;

/// <summary>托盘右键菜单。WPF 圆角可抗锯齿；WinForms Region 裁剪会留下硬边。</summary>
internal sealed class TrayMenuWindow : Window
{
    private static TrayMenuWindow? _open;

    public static void ShowAtCursor(Action restore, Action exit)
    {
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => ShowAtCursor(restore, exit));
            return;
        }

        _open?.Close();
        var win = new TrayMenuWindow(restore, exit);
        _open = win;
        win.Closed += (_, _) => { if (ReferenceEquals(_open, win)) _open = null; };
        win.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceNearCursor(win);
        win.Show();
        win.Activate();
    }

    private TrayMenuWindow(Action restore, Action exit)
    {
        Title = "ForgeDeck";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = false;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Microsoft YaHei");
        FontSize = 13;
        Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Fg);

        var items = new StackPanel();
        items.Children.Add(MakeItem("显示主窗口", restore));
        items.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(8, 4, 8, 4),
            Background = ForgeDeckTheme.Brush(ForgeDeckTheme.Border),
        });
        items.Children.Add(MakeItem("退出", exit));

        var card = new Border
        {
            MinWidth = 160,
            Background = ForgeDeckTheme.Brush(ForgeDeckTheme.Surface),
            BorderBrush = ForgeDeckTheme.Brush(ForgeDeckTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(12),
            Child = items,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.45,
                BlurRadius = 24,
                ShadowDepth = 6,
                Direction = 270,
            },
        };

        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        // 等窗口真正激活后再听 Deactivated，避免 Show/Activate 过程中被立刻关掉
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Deactivated += (_, _) => { if (IsVisible) Close(); };
        });
    }

    private Border MakeItem(string text, Action action)
    {
        var idle = Brushes.Transparent;
        var hover = ForgeDeckTheme.Brush(ForgeDeckTheme.Hover);
        var row = new Border
        {
            Background = idle,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 7, 10, 7),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Fg),
            },
        };
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = idle;
        row.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            action();
        };
        return row;
    }

    private static void PlaceNearCursor(Window win)
    {
        var mouse = WinForms.Control.MousePosition;
        var screen = WinForms.Screen.FromPoint(mouse);
        var scale = DpiScale(win);

        double Dip(int px) => px / scale;
        var size = win.DesiredSize;
        var work = screen.WorkingArea;
        var workLeft = Dip(work.Left);
        var workTop = Dip(work.Top);
        var workRight = Dip(work.Right);
        var workBottom = Dip(work.Bottom);

        var left = Dip(mouse.X);
        var top = Dip(mouse.Y) - size.Height;
        if (top < workTop) top = Dip(mouse.Y);
        if (left + size.Width > workRight) left = workRight - size.Width;
        if (left < workLeft) left = workLeft;
        if (top + size.Height > workBottom) top = workBottom - size.Height;
        if (top < workTop) top = workTop;

        win.Left = left;
        win.Top = top;
    }

    private static double DpiScale(Window win)
    {
        if (Application.Current?.MainWindow is { } main)
        {
            try { return VisualTreeHelper.GetDpi(main).PixelsPerDip; }
            catch (InvalidOperationException) { }
        }
        try { return VisualTreeHelper.GetDpi(win).PixelsPerDip; }
        catch (InvalidOperationException) { return 1; }
    }
}
