using System.ComponentModel;
using System.IO;
using System.Windows;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;
using Microsoft.Web.WebView2.Core;

namespace ForgeDeck.App;

public partial class MainWindow : Window
{
    private readonly ConfigStore _store = new();
    private readonly TerminalSessionManager _terminal = new();
    private readonly ForgeDeckBridge _bridge;
    private bool _confirmedExit;

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
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(14, 18, 17);
        Web.CoreWebView2InitializationCompleted += OnWebReady;
        Loaded += OnWindowLoaded;
        Closing += OnClosing;
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
            MessageBox.Show(
                $"WebView2 初始化失败：{ex.Message}\n请确认已安装 WebView2 运行时。",
                "ForgeDeck", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void OnWebReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Web.CoreWebView2 == null)
        {
            MessageBox.Show(
                $"WebView2 初始化失败：{e.InitializationException?.Message ?? "未知原因"}\n请确认已安装 WebView2 运行时。",
                "ForgeDeck", MessageBoxButton.OK, MessageBoxImage.Error);
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
        var core = Web.CoreWebView2;
        if (core == null) return;
        if (Dispatcher.CheckAccess())
        {
            try { core.PostWebMessageAsJson(message); }
            catch (ObjectDisposedException) { }
        }
        else
        {
            Dispatcher.BeginInvoke(() =>
            {
                try { core.PostWebMessageAsJson(message); }
                catch (ObjectDisposedException) { }
            });
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_confirmedExit || !_terminal.HasRunningSessions || _store.Config.Settings.SkipExitConfirm)
        {
            _terminal.Dispose();
            return;
        }
        e.Cancel = true;
        var running = _terminal.List().Count(s => s.Running);
        var choice = MessageBox.Show(
            $"有 {running} 个会话正在运行，退出将结束它们。确定退出吗？", "ForgeDeck",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Yes)
        {
            _confirmedExit = true;
            Close();
        }
    }
}
