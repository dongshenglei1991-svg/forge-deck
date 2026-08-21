using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ForgeDeck.App;

/// <summary>
/// 最大化 / 向下还原的过渡：逐帧改真实窗口的矩形，屏幕上只有一份内容。
///
/// 为什么不做"截图覆盖层"那类方案：WebView2 是 HWND 宿主，它的内容只能由 Chromium 自己
/// 画。拿旧截图铺一层去补动画，屏幕上就有两份内容 —— 一张和新版面无关的位图，加上真实
/// 重排。位图放大必然模糊，钉住不放大就在边上留一条空带，而真实重排永远是在某一帧一步
/// 到位地发生。只要有两份内容就有得同步，时机、缩放、淡入淡出怎么调都差一点。
///
/// 直接动窗口矩形，Chromium 按帧重排（和拖窗口边缘缩放同一条路径，见 window.beginResize），
/// 没有第二份内容，也就没有"不同步"这回事。
///
/// 三个必要的配合：
/// - 动画期间窗口保持 Normal，逐帧 SetWindowPos 走到工作区，结束才置 Maximized。
///   直接置 Maximized 是一步跳变，没有中间帧可动。
/// - 还原时先把落点钉在当前矩形（WM_WINDOWPOSCHANGING）再置 Normal，否则窗口会瞬移到
///   系统记的 RestoreBounds —— 那一跳正是要动画掉的东西。
/// - 全程关掉 DWM 过渡，免得系统给收尾的状态切换再叠一层自己的动画。
///
/// 每帧只发一次 SetWindowPos：用 WPF 的 Left/Top/Width/Height 会拆成四次，等于一帧里让
/// WebView2 重排四遍。不带 SWP_NOCOPYBITS：保留旧位图、只失效新露出的窄条，比整块客户区
/// 擦掉重画更不容易闪。
///
/// 最小化不在这里：交给系统原生过渡（见 InstallSystemTransitions），它能收向任务栏按钮，
/// 自绘动画做不到。
/// </summary>
internal sealed class WindowMotion
{
    private const double DurationMs = 170;

    private readonly Window _window;
    private readonly Func<Rect> _workAreaPx;
    private Rect? _preMaximizePx;
    private Rect? _pinnedPx;
    private EventHandler? _tick;
    private bool _transitionsSuspended;

    public WindowMotion(Window window, Func<Rect> workAreaPx)
    {
        _window = window;
        _workAreaPx = workAreaPx;
        _window.Closed += (_, _) => StopTick();
    }

    /// <summary>动画进行中：此时别让系统移动/缩放循环插进来抢窗口。</summary>
    public bool Busy { get; private set; }

    /// <summary>WindowStyle=None 的窗口缺少最小化/还原所需的样式位，DWM 的最小化过渡会
    /// 消失；补回 WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX 并明确允许过渡。
    /// 属性 3 是 DWMWA_TRANSITIONS_FORCEDISABLED：TRUE 是关掉动画，不是打开。
    ///
    /// 千万别把 WS_CAPTION 一起补上。它是这几位里唯一影响边框度量的（WS_BORDER|
    /// WS_DLGFRAME），带上它以后 USER32 会把最大化矩形按边框宽度往外扩一圈（实测 125%
    /// 缩放下四边各 7 逻辑像素），而 WindowChrome 把客户区拉平到了整个窗口矩形 ——
    /// 那一圈就成了跑到屏幕外的客户区，右边和下边的内容被切掉。</summary>
    public static void InstallSystemTransitions(IntPtr hwnd)
    {
        var style = Native.GetWindowLong(hwnd, Native.GwlStyle).ToInt64();
        style |= Native.WsSysMenu | Native.WsMinimizeBox | Native.WsMaximizeBox;
        Native.SetWindowLong(hwnd, Native.GwlStyle, new IntPtr(style));
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged);
        SetTransitions(hwnd, enabled: true);
    }

    public void ToggleMaximize()
    {
        if (Busy || _window.WindowState == WindowState.Minimized) return;
        if (_window.WindowState == WindowState.Maximized)
            Restore();
        else
            Maximize();
    }

    /// <summary>WM_WINDOWPOSCHANGING 钩子：把窗口落点钉死在 _pinnedPx。
    /// 只在"脱离最大化但先别动"的那一瞬间生效，其余时间是空操作。</summary>
    public void OnWindowPosChanging(IntPtr lParam)
    {
        if (_pinnedPx is not { } px || px.Width <= 0 || px.Height <= 0) return;
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        pos.X = (int)Math.Round(px.Left);
        pos.Y = (int)Math.Round(px.Top);
        pos.Cx = (int)Math.Round(px.Width);
        pos.Cy = (int)Math.Round(px.Height);
        pos.Flags &= unchecked((uint)~(Native.SwpNoSize | Native.SwpNoMove));
        Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
    }

    private void Maximize()
    {
        var from = WindowRectPx();
        var to = _workAreaPx();
        _preMaximizePx = from;
        if (!Animated || from.IsEmpty || NearlyEqual(from, to))
        {
            _window.WindowState = WindowState.Maximized;
            return;
        }
        SuspendSystemTransitions();
        Animate(from, to, () =>
        {
            // 窗口已经严格落在工作区上，而 WM_GETMINMAXINFO 也把最大化钉在工作区，
            // 所以这一步只改状态、不改几何，看不到跳变。
            _window.WindowState = WindowState.Maximized;
            ResumeSystemTransitions();
        });
    }

    private void Restore()
    {
        var from = WindowRectPx();
        // 经 Aero Snap（拖到屏幕顶边 / Win+↑）最大化时没走过 Maximize()，
        // _preMaximizePx 为空，退回系统记的还原尺寸。
        var to = _preMaximizePx ?? RestoreBoundsPx();
        _preMaximizePx = null;
        if (!Animated || from.IsEmpty || to.IsEmpty || NearlyEqual(from, to))
        {
            _window.WindowState = WindowState.Normal;
            return;
        }
        SuspendSystemTransitions();
        // 先脱离 Maximized 但把落点钉在当前矩形，再由动画走到 to
        _pinnedPx = from;
        _window.WindowState = WindowState.Normal;
        _pinnedPx = null;
        Animate(from, to, ResumeSystemTransitions);
    }

    private void Animate(Rect fromPx, Rect toPx, Action done)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            done();
            return;
        }
        StopTick();
        Busy = true;
        var clock = Stopwatch.StartNew();
        void Tick(object? sender, EventArgs e)
        {
            var aborted = _window.Dispatcher.HasShutdownStarted
                          || _window.WindowState == WindowState.Minimized;
            var t = aborted ? 1 : Math.Min(1.0, clock.Elapsed.TotalMilliseconds / DurationMs);
            if (!aborted)
                Place(hwnd, Lerp(fromPx, toPx, OutCubic(t)));
            if (t < 1) return;
            StopTick();
            Busy = false;
            if (!aborted)
                Place(hwnd, toPx); // 收尾精确落位，抹掉逐帧取整的残差
            try { done(); }
            catch
            {
                ResumeSystemTransitions();
                throw;
            }
        }
        _tick = Tick;
        CompositionTarget.Rendering += Tick;
    }

    private static void Place(IntPtr hwnd, Rect px) =>
        Native.SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(px.Left), (int)Math.Round(px.Top),
            (int)Math.Round(px.Width), (int)Math.Round(px.Height),
            Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);

    private void StopTick()
    {
        if (_tick == null) return;
        CompositionTarget.Rendering -= _tick;
        _tick = null;
    }

    /// <summary>用户在系统设置里关了窗口动画时不自作多情。</summary>
    private static bool Animated => SystemParameters.ClientAreaAnimation;

    private Rect WindowRectPx()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rc)) return Rect.Empty;
        if (rc.Right <= rc.Left || rc.Bottom <= rc.Top) return Rect.Empty;
        return new Rect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    private Rect RestoreBoundsPx()
    {
        var dip = _window.RestoreBounds;
        if (dip.IsEmpty) return Rect.Empty;
        var dpi = VisualTreeHelper.GetDpi(_window);
        return new Rect(dip.Left * dpi.DpiScaleX, dip.Top * dpi.DpiScaleY,
            dip.Width * dpi.DpiScaleX, dip.Height * dpi.DpiScaleY);
    }

    /// <summary>动画期间关掉 DWM 过渡：收尾的 WindowState 切换不该再被系统演一遍。</summary>
    private void SuspendSystemTransitions()
    {
        if (_transitionsSuspended) return;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetTransitions(hwnd, enabled: false);
        _transitionsSuspended = true;
    }

    private void ResumeSystemTransitions()
    {
        if (!_transitionsSuspended) return;
        _transitionsSuspended = false;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetTransitions(hwnd, enabled: true);
    }

    private static void SetTransitions(IntPtr hwnd, bool enabled)
    {
        int disable = enabled ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DwmwaTransitionsForceDisabled, ref disable, sizeof(int));
    }

    private static Rect Lerp(Rect a, Rect b, double t) => new(
        a.Left + (b.Left - a.Left) * t,
        a.Top + (b.Top - a.Top) * t,
        a.Width + (b.Width - a.Width) * t,
        a.Height + (b.Height - a.Height) * t);

    private static double OutCubic(double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return 1 - Math.Pow(1 - t, 3);
    }

    private static bool NearlyEqual(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) < 2 && Math.Abs(a.Top - b.Top) < 2
        && Math.Abs(a.Width - b.Width) < 2 && Math.Abs(a.Height - b.Height) < 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class Native
    {
        internal const int GwlStyle = -16;
        internal const int WsSysMenu = 0x00080000;
        internal const int WsMinimizeBox = 0x00020000;
        internal const int WsMaximizeBox = 0x00010000;
        internal const int SwpNoSize = 0x0001;
        internal const int SwpNoMove = 0x0002;
        internal const int SwpNoZOrder = 0x0004;
        internal const int SwpNoActivate = 0x0010;
        internal const int SwpFrameChanged = 0x0020;
        internal const int SwpNoOwnerZOrder = 0x0200;
        internal const int DwmwaTransitionsForceDisabled = 3;

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

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    }
}
