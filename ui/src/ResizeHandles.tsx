import { useEffect, useState, type MouseEvent } from 'react';
import { bridge } from './bridge';

const EDGES = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'] as const;
type Edge = (typeof EDGES)[number];

/** WebView2 盖住客户区，系统缩放边框收不到鼠标；用页面边缘热区转发到 window.beginResize。 */
export function ResizeHandles() {
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

  if (maximized) return null;

  const onDown = (edge: Edge) => (e: MouseEvent<HTMLElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    bridge.request('window.beginResize', { edge }).catch(() => {});
  };

  return (
    <div className="resize-handles" aria-hidden="true">
      {EDGES.map((edge) => (
        <div key={edge} className={`resize-handle resize-${edge}`} onMouseDown={onDown(edge)} />
      ))}
    </div>
  );
}
