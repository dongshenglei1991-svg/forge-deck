using System.Collections;
using System.Text;
using Porta.Pty;

namespace ForgeDeck.Core.Terminal;

public sealed record TerminalSessionInfo(string SessionId, string Title, string Workdir, bool Running, int? ExitCode);

public sealed class TerminalSessionManager : IDisposable
{
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Session> _sessions = new();
    private readonly object _gate = new();

    /// <summary>终端输出（sessionId, chunk，UTF-8 已解码）。</summary>
    public event Action<string, string>? Output;
    /// <summary>进程退出（sessionId, exitCode；每个会话恰好触发一次）。</summary>
    public event Action<string, int>? Exited;
    /// <summary>会话列表或运行状态变化。</summary>
    public event Action? Changed;

    public async Task<string> CreateAsync(
        string title, string app, IReadOnlyList<string> args, string workdir,
        IReadOnlyDictionary<string, string>? env = null, int cols = 120, int rows = 30)
    {
        // 合并全量环境变量，避免子进程丢 PATH 等基础变量
        var merged = new Dictionary<string, string>();
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string)e.Value!;
        if (env != null)
            foreach (var (key, value) in env)
                merged[key] = value;

        var id = Guid.NewGuid().ToString("N");
        // Porta.Pty 非 verbatim 模式会给每个参数加引号（含 "/c"），cmd.exe 无法解析；
        // 改用 verbatim + 仅给含空白的参数加引号（与 LaunchService.QuoteIfSpaced 约定一致），
        // App 路径的引号始终由 Porta 负责（带空格路径已验证）。
        var connection = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = title,
            Cols = cols,
            Rows = rows,
            Cwd = workdir,
            App = app,
            CommandLine = args.Select(QuoteIfSpaced).ToArray(),
            VerbatimCommandLine = true,
            Environment = merged,
        }, CancellationToken.None);

        var session = new Session(id, title, workdir, connection);
        lock (_gate) { _sessions[id] = session; }
        connection.ProcessExited += (_, e) => AnnounceExit(session, e.ExitCode);
        _ = PumpOutputAsync(session);
        // 极端竞态：进程在订阅事件前已退出（事件已丢）——主动探测补报，恰好一次语义由 AnnounceExit 保证
        try { if (connection.WaitForExit(0)) AnnounceExit(session, SafeExitCode(session)); }
        catch { }
        Changed?.Invoke();
        return id;
    }

    /// <summary>含空白的参数加引号，其余原样（Windows 命令行惯例）。</summary>
    private static string QuoteIfSpaced(string token) =>
        token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    /// <summary>标记退出并广播 Exited/Changed，恰好一次（Porta 事件与主动补报可能竞争）。</summary>
    private void AnnounceExit(Session session, int exitCode)
    {
        if (!session.TryMarkExited(exitCode)) return;
        Exited?.Invoke(session.Id, exitCode);
        Changed?.Invoke();
    }

    private static int SafeExitCode(Session session)
    {
        try { return session.Connection.ExitCode; }
        catch { return -1; }
    }

    private async Task PumpOutputAsync(Session session)
    {
        var buffer = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        // 有状态解码器：跨 chunk 的多字节 UTF-8 序列不会裂成 U+FFFD
        var decoder = Encoding.UTF8.GetDecoder();
        try
        {
            while (true)
            {
                var read = await session.Connection.ReaderStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read <= 0) break;
                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                if (count > 0)
                    Output?.Invoke(session.Id, new string(chars, 0, count));
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    public void Write(string sessionId, string data)
    {
        var session = Get(sessionId);
        var bytes = Encoding.UTF8.GetBytes(data);
        session.Connection.WriterStream.Write(bytes, 0, bytes.Length);
        session.Connection.WriterStream.Flush();
    }

    public void Resize(string sessionId, int cols, int rows) => Get(sessionId).Connection.Resize(cols, rows);

    public void Kill(string sessionId)
    {
        Session? session;
        lock (_gate) { _sessions.TryGetValue(sessionId, out session); }
        if (session == null || !session.Running) return;
        try { session.Connection.Kill(); } catch { }
    }

    /// <summary>关闭并从列表移除会话（标签页 × 按钮）。kill/等退出/释放连接放后台执行：
    /// ① 立即 Dispose 会拆掉 Porta 的退出监视（Process.Exited 先退订），Exited 事件永远不发；
    /// ② 若在本方法内同步等退出，WaitForExit 会在调用线程上同步触发 Process.Exited——
    ///    而调用方往往在 Close 返回后才订阅 Exited，同步触发必然错过。后台化让事件一定晚于 Close 返回。</summary>
    public void Close(string sessionId)
    {
        Session? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session)) return;
            _sessions.Remove(sessionId);
        }
        _ = Task.Run(() =>
        {
            try
            {
                if (session.Running)
                {
                    session.Connection.Kill();
                    session.Connection.WaitForExit((int)CloseWaitTimeout.TotalMilliseconds);
                    // 事件未及送达（或 kill 失败）则补报，恰好一次
                    if (session.Running) AnnounceExit(session, SafeExitCode(session));
                }
            }
            catch { }
            finally { session.Dispose(); }
        });
        Changed?.Invoke();
    }

    public void KillAll()
    {
        List<Session> running;
        lock (_gate) running = _sessions.Values.Where(s => s.Running).ToList();
        foreach (var session in running)
            try { session.Connection.Kill(); } catch { }
    }

    public bool HasRunningSessions
    {
        get { lock (_gate) return _sessions.Values.Any(s => s.Running); }
    }

    public IReadOnlyList<TerminalSessionInfo> List()
    {
        lock (_gate)
            return _sessions.Values
                .OrderBy(s => s.StartedAt)
                .Select(s => new TerminalSessionInfo(s.Id, s.Title, s.Workdir, s.Running, s.Running ? null : s.ExitCode))
                .ToList();
    }

    private Session Get(string sessionId)
    {
        lock (_gate)
            return _sessions.TryGetValue(sessionId, out var s)
                ? s : throw new KeyNotFoundException($"会话不存在：{sessionId}");
    }

    public void Dispose()
    {
        List<Session> all;
        lock (_gate)
        {
            all = _sessions.Values.ToList();
            _sessions.Clear();
        }
        foreach (var s in all)
        {
            try
            {
                if (s.Running)
                {
                    s.Connection.Kill();
                    s.Connection.WaitForExit((int)CloseWaitTimeout.TotalMilliseconds);
                    if (s.Running) AnnounceExit(s, SafeExitCode(s));
                }
            }
            catch { }
            s.Dispose();
        }
    }

    private sealed class Session(string id, string title, string workdir, IPtyConnection connection) : IDisposable
    {
        private int _exitAnnounced;

        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Workdir { get; } = workdir;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public IPtyConnection Connection { get; } = connection;
        public bool Running { get; private set; } = true;
        public int ExitCode { get; private set; } = -1;

        /// <summary>记录退出状态；返回 false 表示已报过（防 Porta 事件与主动补报双发）。</summary>
        public bool TryMarkExited(int exitCode)
        {
            if (Interlocked.Exchange(ref _exitAnnounced, 1) == 1) return false;
            ExitCode = exitCode;
            Running = false;
            return true;
        }

        public void Dispose() => Connection.Dispose();
    }
}
