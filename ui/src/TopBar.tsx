function initials(name: string): string {
  const parts = name.split(/[.\-_ ]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase() || 'F/';
}

export function TopBar({ title, userName, onRefresh }: { title: string; userName: string; onRefresh: () => void }) {
  return (
    <header className="top">
      <div className="crumb">工作台&nbsp; / &nbsp;<b>{title}</b></div>
      <div className="top-actions">
        <button className="icon-btn" title="刷新工具扫描" onClick={onRefresh}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M20 11a8.1 8.1 0 0 0-14.8-4L3 10m0-5v5h5M4 13a8.1 8.1 0 0 0 14.8 4L21 14m0 5v-5h-5" /></svg>
        </button>
        <button className="icon-btn" title="通知" tabIndex={-1}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" /></svg>
        </button>
        <div className="avatar">{initials(userName)}</div>
      </div>
    </header>
  );
}
