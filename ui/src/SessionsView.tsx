import type { TerminalSessionInfo } from './types';

export function SessionsView({ sessions, onNewShell }: {
  sessions: TerminalSessionInfo[]; onNewShell: () => void;
}) {
  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">TERMINAL SESSIONS / 03</p>
          <h1 className="title">终端会话</h1>
          <p className="sub">管理正在运行的工具、工作目录和会话状态。</p>
        </div>
        <button className="btn" onClick={onNewShell}>新建空白会话</button>
      </div>
      <div className="session-grid">
        {sessions.length === 0 &&
          <p className="sub" style={{ gridColumn: '1 / -1' }}>暂无会话。启动工具或新建空白会话后在此查看。</p>}
        {sessions.map((s) => (
          <article key={s.sessionId} className="session-card">
            <div>
              <h2>{s.title}</h2>
              <p className="mono">{s.workdir}</p>
            </div>
            <span className="status-text">
              {s.running ? '运行中' : `已退出${s.exitCode != null ? ` · ${s.exitCode}` : ''}`}
            </span>
          </article>
        ))}
      </div>
    </>
  );
}
