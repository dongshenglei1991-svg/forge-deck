using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ForgeDeck.App;

/// <summary>
/// 主窗口外侧的主题色光晕。WebView2 铺满不透明 HWND，CSS 阴影出不了窗体；
/// 另开一层透明窗口垫在后面，用 DropShadow 把光晕画到桌面上。
/// 最大化 / 最小化 / 隐藏时收起（工作区贴边没有外侧可画）。
/// </summary>
internal sealed class WindowGlow : IDisposable
{
    private const double PadDip = 32;
    private readonly Window _owner;
    private readonly Window _glow;
    private readonly Border _rim;
    private readonly DropShadowEffect _fx;
    private bool _disposed;
    private bool _syncing;

    public WindowGlow(Window owner)
    {
        _owner = owner;
        _fx = new DropShadowEffect
        {
            ShadowDepth = 0,
            Direction = 0,
            BlurRadius = 28,
            Opacity = 0.85,
            Color = ForgeDeckTheme.Accent,
        };
        _rim = new Border
        {
            Margin = new Thickness(PadDip),
            BorderThickness = new Thickness(1),
            Effect = _fx,
        };
        ApplyColor();

        _glow = new Window
        {
            Title = "ForgeDeck Glow",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            IsHitTestVisible = false,
            Focusable = false,
            Content = _rim,
        };
        _glow.SourceInitialized += (_, _) => ApplyExStyle();
        _owner.IsVisibleChanged += (_, _) => Sync();
        _owner.StateChanged += (_, _) => Sync();
        _owner.Closed += (_, _) => Dispose();
    }

    public void ApplyColor()
    {
        var accent = ForgeDeckTheme.Accent;
        _fx.Color = accent;
        _rim.BorderBrush = ForgeDeckTheme.Brush(accent);
        // 实心底：阴影需要不透明剪影；主窗会盖住中间，只露出外侧晕
        _rim.Background = ForgeDeckTheme.Brush(ForgeDeckTheme.Bg);
    }

    public void Sync()
    {
        if (_disposed || _syncing) return;
        _syncing = true;
        try
        {
            if (!_owner.IsVisible || _owner.WindowState != WindowState.Normal)
            {
                if (_glow.IsVisible) _glow.Hide();
                return;
            }

            var ownerHwnd = new WindowInteropHelper(_owner).Handle;
            if (ownerHwnd == IntPtr.Zero) return;

            if (!_glow.IsVisible) _glow.Show();
            var glowHwnd = new WindowInteropHelper(_glow).Handle;
            if (glowHwnd == IntPtr.Zero) return;

            if (!Native.GetWindowRect(ownerHwnd, out var rect)) return;
            var dpi = VisualTreeHelper.GetDpi(_owner);
            var pad = (int)Math.Ceiling(PadDip * dpi.DpiScaleX);
            Native.SetWindowPos(
                glowHwnd, ownerHwnd,
                rect.Left - pad, rect.Top - pad,
                rect.Right - rect.Left + pad * 2,
                rect.Bottom - rect.Top + pad * 2,
                Native.SwpNoActivate);
        }
        finally { _syncing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _glow.Close(); }
        catch (InvalidOperationException) { }
    }

    private void ApplyExStyle()
    {
        var hwnd = new WindowInteropHelper(_glow).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = Native.GetWindowLong(hwnd, Native.GwlExStyle).ToInt64();
        ex |= Native.WsExToolWindow | Native.WsExNoActivate | Native.WsExTransparent;
        Native.SetWindowLong(hwnd, Native.GwlExStyle, new IntPtr(ex));
    }

    private static class Native
    {
        internal const int GwlExStyle = -20;
        internal const int WsExTransparent = 0x00000020;
        internal const int WsExToolWindow = 0x00000080;
        internal const int WsExNoActivate = 0x08000000;
        internal const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        internal static IntPtr GetWindowLong(IntPtr hwnd, int index) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

        internal static void SetWindowLong(IntPtr hwnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, index, value);
            else SetWindowLong32(hwnd, index, value.ToInt32());
        }
    }
}
