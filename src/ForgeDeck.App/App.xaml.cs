using System.IO;
using System.Windows;

namespace ForgeDeck.App;

public partial class App : Application
{
    private const string MutexName = @"Local\ForgeDeck.SingleInstance";
    private const string ShowEventName = @"Local\ForgeDeck.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showCts;

    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "forgedeck-crash.log");

    // 弹窗去重：同一异常可能随消息循环反复抛出（如每个布局帧一次），
    // 5 秒内只弹一次，其余仅记日志，避免弹窗风暴把应用锁死
    private static readonly object PromptGate = new();
    private static DateTime _lastPromptUtc = DateTime.MinValue;
    private static string _lastPromptKey = "";

    private static void Log(string kind, Exception ex)
    {
        try
        {
            File.AppendAllText(CrashLog,
                $"[{DateTime.Now:HH:mm:ss.fff}] {kind}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n---\n");
        }
        catch { }
    }

    private static void Report(string kind, Exception ex, bool fatal)
    {
        Log(kind, ex);
        lock (PromptGate)
        {
            var key = $"{ex.GetType().FullName}|{ex.Message}";
            if (!fatal && key == _lastPromptKey && DateTime.UtcNow - _lastPromptUtc < TimeSpan.FromSeconds(5))
                return;
            _lastPromptKey = key;
            _lastPromptUtc = DateTime.UtcNow;
        }
        if (Current?.Dispatcher.HasShutdownStarted == true) return;
        var head = fatal
            ? "发生未处理的异常，程序即将退出。"
            : "发生未处理的异常，已拦截以避免闪退，可继续使用。";
        try
        {
            MessageBox.Show(
                $"{head}\n\n{ex.GetType().Name}: {ex.Message}\n\n详细信息已记录到：\n{CrashLog}",
                "ForgeDeck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidOperationException) { } // 关窗期间不能再弹窗
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showCts = new CancellationTokenSource();
        var token = _showCts.Token;
        var ev = _showEvent;
        var waiter = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!ev.WaitOne(TimeSpan.FromMilliseconds(500))) continue;
                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (MainWindow is MainWindow win)
                            win.RestoreFromTray();
                    });
                }
                catch (InvalidOperationException) { }
            }
        })
        {
            IsBackground = true,
            Name = "ForgeDeck.ShowWindow",
        };
        waiter.Start();

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Report("DispatcherUnhandledException", args.Exception, fatal: false);
            args.Handled = true; // 提示后吞掉，避免直接闪退（会话确认等关键错误仍会表现在 UI 行为上）
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report("AppDomain.UnhandledException",
                args.ExceptionObject as Exception ?? new Exception("non-exception object"), fatal: true);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // 后台任务的异常要到 GC 时才浮出，且已不影响运行，弹窗只会造成困扰，仅记日志
            Log("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showCts?.Cancel();
        try { _showEvent?.Set(); } catch (ObjectDisposedException) { }
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void SignalExistingInstance()
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
