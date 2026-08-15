export type View = 'launcher' | 'tools' | 'sessions' | 'settings';

const ICONS = {
  launcher: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="4" width="6" height="6" /><rect x="14" y="4" width="6" height="6" /><rect x="4" y="14" width="6" height="6" /><rect x="14" y="14" width="6" height="6" /></svg>,
  tools: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M14.7 6.3a4.2 4.2 0 0 0-5.9 5.9L4 17v3h3l4.8-4.8a4.2 4.2 0 0 0 5.9-5.9l-2.1 2.1-2.8-2.8 1.9-2.3Z" /></svg>,
  sessions: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="m7 9 3 3-3 3m5 0h5" /></svg>,
  settings: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L7 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H5.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L7 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" /></svg>,
};

export function Rail({ view, onView, version }: { view: View; onView: (v: View) => void; version: string }) {
  const item = (v: View, label: string) => (
    <button className={view === v ? 'active' : ''} aria-current={view === v ? 'page' : undefined} onClick={() => onView(v)}>
      {ICONS[v]}<span>{label}</span>
    </button>
  );
  return (
    <aside className="rail">
      <div className="brand">
        <div className="brand-mark">F/</div>
        <div><strong>forge</strong><small>TOOL LAUNCHER</small></div>
      </div>
      <div className="nav-label">工作台</div>
      <nav className="nav" aria-label="工作台">
        {item('launcher', '快速启动')}
        {item('tools', '工具库')}
        {item('sessions', '终端会话')}
      </nav>
      <div className="nav-label">系统</div>
      <nav className="nav" aria-label="系统">{item('settings', '设置')}</nav>
      <div className="rail-foot">
        <span className="status-dot" />扫描服务正常<br />
        <span className="mono">{version || 'v0.1.0 · Windows'}</span>
      </div>
    </aside>
  );
}
