using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ForgeDeck.Core;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace ForgeDeck.App;

public partial class MainWindow : Window
{
    private readonly ConfigStore _store = new();
    private readonly TerminalSessionManager _terminal = new();
    private readonly ForgeDeckBridge _bridge;
    private readonly TrayIconHost _tray = new();
    private bool _confirmedExit;
    private bool _forceExit;
    private WindowState _trayRestoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[]
            {
                new KnownDirsScanSource(),
                new PathScanSource(),
                new RegistryScanSource(new RegistryUninstallRegistry()),
                new StartMenuScanSource(new WScriptShellLinkResolver()),
                new ExtraDirsScanSource(), // 规格 §4.1 数据源 #6：附加目录，最低优先级
            }),
            _terminal);
        _bridge.Dispatcher.Outgoing += Post;
        RegisterWindowMethods();
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(14, 18, 17);
        Web.CoreWebView2InitializationCompleted += OnWebReady;
        Loaded += OnWindowLoaded;
        Closing += OnClosing;
        if (System.Windows.Application.Current != null)
            System.Windows.Application.Current.SessionEnding += (_, _) => _forceExit = true;
        _tray.RestoreRequested += RestoreFromTray;
        _tray.ExitRequested += ExitFromTray;
    }

    /// <summary>窗口操作与系统对话框属 UI 能力，在 App 层注册（Core 不依赖 WPF）。</summary>
    private void RegisterWindowMethods()
    {
        var d = _bridge.Dispatcher;
        d.Register("window.minimize", _ =>
        {
            WindowState = WindowState.Minimized;
            return Task.FromResult<object?>(null);
        });
        d.Register("window.toggleMaximize", _ =>
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return Task.FromResult<object?>(null);
        });
        d.Register("window.close", _ =>
        {
            Close();
            return Task.FromResult<object?>(null);
        });
        d.Register("window.hideToTray", _ =>
        {
            HideToTray();
            return Task.FromResult<object?>(null);
        });
        d.Register("window.exit", _ =>
        {
            _forceExit = true;
            Close();
            return Task.FromResult<object?>(null);
        });
        d.Register("window.confirmExit", _ =>
        {
            _confirmedExit = true;
            _forceExit = true;
            Close();
            return Task.FromResult<object?>(null);
        });
        d.Register("window.beginDrag", _ =>
        {
            BeginDrag();
            return Task.FromResult<object?>(null);
        });
        d.Register("window.getState", _ =>
            Task.FromResult<object?>(new { maximized = WindowState == WindowState.Maximized }));
        d.Register("dialog.selectDirectory", p =>
        {
            string? initial = null;
            try { initial = p?.GetProperty("initial").GetString(); } catch (KeyNotFoundException) { }
            string? result = null;
            var dlg = new OpenFolderDialog { Title = "选择工作文件夹" };
            if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                dlg.InitialDirectory = initial;
            if (dlg.ShowDialog(this) == true)
                result = dlg.FolderName;
            return Task.FromResult<object?>(result == null ? null : new { path = result });
        });
        d.Register("dialog.selectFile", p =>
        {
            string? initial = null;
            try { initial = p?.GetProperty("initial").GetString(); } catch (KeyNotFoundException) { }
            var dlg = new OpenFileDialog
            {
                Title = "选择可执行文件",
                Filter = "可执行文件|*.exe;*.cmd;*.bat;*.ps1|所有文件|*.*",
            };
            if (!string.IsNullOrEmpty(initial))
            {
                if (File.Exists(initial))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(initial);
                    dlg.FileName = Path.GetFileName(initial);
                }
                else if (Directory.Exists(initial))
                    dlg.InitialDirectory = initial;
            }
            string? result = null;
            if (dlg.ShowDialog(this) == true)
                result = dlg.FileName;
            return Task.FromResult<object?>(result == null ? null : new { path = result });
        });
        StateChanged += (_, _) =>
        {
            // 最大化时关掉调整边框，避免 WindowChrome 再往工作区外扩一圈
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
                chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
                    ? new Thickness(0) : new Thickness(8);
            d.Emit("window.state.changed", new { maximized = WindowState == WindowState.Maximized });
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    /// <summary>无边框窗口默认最大化到整块屏幕（含任务栏）。按显示器工作区回写
    /// WM_GETMINMAXINFO，底部/侧边任务栏都不再压住内容。</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_GETMINMAXINFO)
        {
            handled = Win32.TryApplyWorkArea(hwnd, lParam);
        }
        else if (msg == Win32.WM_QUERYENDSESSION)
        {
            _forceExit = true;
        }
        return IntPtr.Zero;
    }

    // 部分环境下 WPF WebView2 控件的自动初始化（内部走控件自己的环境创建路径）会无限挂起，
    // 表现为窗口白屏且无 msedgewebview2 子进程。改为手动创建环境再喂给控件，绕开该路径。
    // AdditionalBrowserArguments：发布模式以 file:// 直载 wwwroot，ES module 脚本会被
    // Chromium 按 CORS 拦截，需放开 file 页面对 file 资源的访问。
    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var udf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ForgeDeck", "WebView2");
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--allow-file-access-from-files",
            };
            var env = await CoreWebView2Environment.CreateAsync(null, udf, options);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            AppPrompt.Alert(this,
                "无法启动",
                $"WebView2 初始化失败：{ex.Message}\n请确认已安装 WebView2 运行时。");
            Close();
        }
    }

    private void OnWebReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Web.CoreWebView2 == null)
        {
            AppPrompt.Alert(this,
                "无法启动",
                $"WebView2 初始化失败：{e.InitializationException?.Message ?? "未知原因"}\n请确认已安装 WebView2 运行时。");
            Close();
            return;
        }
        var core = Web.CoreWebView2;
        core.WebMessageReceived += async (_, args) =>
        {
            var response = await _bridge.Dispatcher.HandleAsync(args.TryGetWebMessageAsString());
            if (response != null) Post(response);
        };
        if (Environment.GetEnvironmentVariable("FORGEDECK_DEV") == "1")
            core.Navigate("http://localhost:5173");
        else
            core.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html")).AbsoluteUri);
    }

    private void Post(string message)
    {
        // WebView2 必须在 UI 线程摸；终端退出事件来自线程池，先切线程再读属性
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess())
        {
            try { Dispatcher.BeginInvoke(() => Post(message)); }
            catch (InvalidOperationException) { }
            return;
        }
        try
        {
            var core = Web.CoreWebView2;
            if (core == null) return;
            core.PostWebMessageAsJson(message);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { } // 关窗期 / 控件未就绪
    }

    /// <summary>鼠标按下发生在 WebView2 子窗口内，WPF 输入系统看不到（Mouse.LeftButton 始终为 Released），
    /// Window.DragMove() 因此必抛 InvalidOperationException。改用真实按键状态判定，
    /// 以 HTCAPTION 非客户区消息进入系统原生移动循环。</summary>
    private void BeginDrag()
    {
        if (WindowState == WindowState.Maximized) return;
        if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) == 0) return; // 往返期间左键已松开
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.ReleaseCapture();
        Win32.SendMessage(hwnd, Win32.WM_NCLBUTTONDOWN, (IntPtr)Win32.HTCAPTION, IntPtr.Zero);
    }

    private static class Win32
    {
        internal const int VK_LBUTTON = 0x01;
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int WM_GETMINMAXINFO = 0x0024;
        internal const int WM_QUERYENDSESSION = 0x0011;
        internal const int HTCAPTION = 0x2;
        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        internal static bool TryApplyWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var place = MaximizeWorkArea.FromMonitor(
                info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom,
                info.Monitor.Left, info.Monitor.Top);
            mmi.MaxPosition = new Point { X = place.X, Y = place.Y };
            mmi.MaxSize = new Point { X = place.Width, Y = place.Height };
            Marshal.StructureToPtr(mmi, lParam, fDeleteOld: true);
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public int Flags;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var action = CloseDecision.Resolve(_store.Config.Settings.CloseBehavior, _forceExit);
        if (action == CloseAction.Ask)
        {
            e.Cancel = true;
            if (Web.CoreWebView2 != null)
                _bridge.Dispatcher.Emit("window.close.prompt", new { });
            else
            {
                var fallback = AppPrompt.Ask(this,
                    "关闭 ForgeDeck？",
                    "可以把窗口藏到托盘，会话继续跑。",
                    "退出应用", "最小化到托盘");
                if (fallback == PromptChoice.Primary) HideToTray();
                else if (fallback == PromptChoice.Secondary)
                {
                    _forceExit = true;
                    Dispatcher.BeginInvoke(Close);
                }
            }
            return;
        }
        if (action == CloseAction.HideToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_confirmedExit || !_terminal.HasRunningSessions || _store.Config.Settings.SkipExitConfirm)
        {
            _tray.Dispose();
            _terminal.Dispose();
            return;
        }
        e.Cancel = true;
        _forceExit = false;
        var running = _terminal.List().Count(s => s.Running);
        if (Web.CoreWebView2 != null)
        {
            _bridge.Dispatcher.Emit("window.exit.confirm", new { running });
            return;
        }
        if (!AppPrompt.Confirm(this, "确定退出？",
            $"有 {running} 个会话正在运行，退出将结束它们。", "退出", "取消"))
            return;
        _confirmedExit = true;
        _forceExit = true;
        Dispatcher.BeginInvoke(Close);
    }

    private void HideToTray()
    {
        if (_tray.Visible) return;
        _trayRestoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        ShowInTaskbar = false;
        Hide();
        if (!_tray.Show())
        {
            AppPrompt.Alert(this, "无法创建托盘图标",
                "窗口已隐藏。再次启动 ForgeDeck 可恢复窗口。");
        }
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = _trayRestoreState;
        Activate();
        Topmost = true;
        Topmost = false;
        _tray.Hide();
    }

    private void ExitFromTray()
    {
        RestoreFromTray();
        _forceExit = true;
        Close();
    }
}
