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
        Closing += OnClosing;
    }

    private void OnWebReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
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
        if (Web.CoreWebView2 == null) return;
        if (Dispatcher.CheckAccess()) Web.CoreWebView2.PostWebMessageAsJson(message);
        else Dispatcher.BeginInvoke(() => Web.CoreWebView2.PostWebMessageAsJson(message));
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
