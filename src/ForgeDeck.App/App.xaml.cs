using System.IO;
using System.Windows;

namespace ForgeDeck.App;

public partial class App : Application
{
    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "forgedeck-crash.log");

    private static void Log(string kind, Exception ex)
    {
        try
        {
            File.AppendAllText(CrashLog,
                $"[{DateTime.Now:HH:mm:ss.fff}] {kind}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n---\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log("DispatcherUnhandledException", args.Exception);
            args.Handled = true; // 记录后吞掉，避免直接闪退（会话确认等关键错误仍会表现在 UI 行为上）
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain.UnhandledException", args.ExceptionObject as Exception ?? new Exception("non-exception object"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }
}
