import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

export function WorkdirControl({ value, recent, onChange, onBrowse }: {
  value: string; recent: string[]; onChange: (v: string) => void; onBrowse: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [rect, setRect] = useState<DOMRect | null>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const update = () => setRect(ref.current?.getBoundingClientRect() ?? null);
    update();
    const onClick = (e: MouseEvent) => {
      const t = e.target as Node;
      if (ref.current?.contains(t) || menuRef.current?.contains(t)) return;
      setMenuOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false); };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    return () => {
      document.removeEventListener('click', onClick);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, [menuOpen]);

  return (
    <div className="workdir-control" ref={ref}>
      <input className="input mono" id="workdir" value={value} onChange={(e) => onChange(e.target.value)} />
      <button className="workdir-btn" type="button" aria-label="打开最近工作目录"
        aria-expanded={menuOpen} aria-controls={menuOpen ? 'workdirMenu' : undefined}
        onClick={() => setMenuOpen((v) => !v)}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="m6 9 6 6 6-6" /></svg>
      </button>
      <button className="workdir-btn" type="button" aria-label="选择工作文件夹" onClick={onBrowse}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M3 7.5h7l2 2h9v9H3z" /><path d="M3 7.5V5h7l2 2.5" /></svg>
      </button>
      {menuOpen && rect && createPortal(
        <div className="menu-popup menu-popup-fixed" id="workdirMenu" role="menu" ref={menuRef}
          style={{ top: rect.bottom + 6, left: rect.left, width: Math.max(160, rect.width - 39) }}>
          <div className="menu-title">最近与常用目录</div>
          {recent.length === 0 && <div className="menu-option muted">暂无历史记录</div>}
          {recent.slice(0, 5).map((p) => (
            <button key={p} className="menu-option mono" type="button" role="menuitem"
              onClick={() => { onChange(p); setMenuOpen(false); }}>{p}</button>
          ))}
        </div>,
        document.body,
      )}
    </div>
  );
}
