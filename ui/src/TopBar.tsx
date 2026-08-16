import { useEffect, useState, type MouseEvent } from 'react';
import { bridge } from './bridge';

function initials(name: string): string {
  const parts = name.split(/[.\-_ ]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase() || 'F/';
}

export function TopBar({ title, userName, onRefresh }: { title: string; userName: string; onRefresh: () => void }) {
  const [maximized, setMaximized] = useState(false);

  useEffect(() => {
    let disposed = false;
    bridge.request<{ maximized: boolean }>('window.getState')
      .then((s) => { if (!disposed && s) setMaximized(s.maximized); })
      .catch(() => {});
    const off = bridge.on('window.state.changed', (d: { maximized?: boolean }) => {
      if (d && typeof d.maximized === 'boolean') setMaximized(d.maximized);
    });
    return () => { disposed = true; off(); };
  }, []);

  // 拖拽标题栏：排除按钮区域；双击切换最大化
  const onDrag = (e: MouseEvent<HTMLElement>) => {
    if (e.button !== 0) return;
    if ((e.target as HTMLElement).closest('button')) return;
    bridge.request('window.beginDrag').catch(() => {});
  };
  const onDoubleClickBar = (e: MouseEvent<HTMLElement>) => {
    if ((e.target as HTMLElement).closest('button')) return;
    bridge.request('window.toggleMaximize').catch(() => {});
  };

  return (
    <header className="top" onMouseDown={onDrag} onDoubleClick={onDoubleClickBar}>
      <div className="crumb">工作台&nbsp; / &nbsp;<b>{title}</b></div>
      <div className="top-actions">
        <button className="icon-btn" title="刷新工具扫描" onClick={onRefresh}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M20 11a8.1 8.1 0 0 0-14.8-4L3 10m0-5v5h5M4 13a8.1 8.1 0 0 0 14.8 4L21 14m0 5v-5h-5" /></svg>
        </button>
        <button className="icon-btn" title="通知" tabIndex={-1}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" /></svg>
        </button>
        <div className="avatar">{initials(userName)}</div>
        <div className="win-group">
          <button className="win-btn" title="最小化" aria-label="最小化"
            onClick={() => bridge.request('window.minimize').catch(() => {})}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M5 12h14" /></svg>
          </button>
          <button className="win-btn" title={maximized ? '向下还原' : '最大化'} aria-label={maximized ? '向下还原' : '最大化'}
            onClick={() => bridge.request('window.toggleMaximize').catch(() => {})}>
            {maximized
              ? <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="9" y="9" width="10" height="10" rx="1" /><path d="M15 9V6a1 1 0 0 0-1-1H6a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h3" /></svg>
              : <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="5" y="5" width="14" height="14" rx="1" /></svg>}
          </button>
          <button className="win-btn close" title="关闭" aria-label="关闭"
            onClick={() => bridge.request('window.close').catch(() => {})}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M6 6l12 12M18 6 6 18" /></svg>
          </button>
        </div>
      </div>
    </header>
  );
}
