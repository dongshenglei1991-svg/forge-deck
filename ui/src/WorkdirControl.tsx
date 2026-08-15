import { useEffect, useRef, useState } from 'react';

export function WorkdirControl({ value, recent, onChange, onBrowse }: {
  value: string; recent: string[]; onChange: (v: string) => void; onBrowse: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const onClick = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setMenuOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false); };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('click', onClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [menuOpen]);

  return (
    <div className="workdir-control" ref={ref}>
      <input className="input mono" id="workdir" value={value} onChange={(e) => onChange(e.target.value)} />
      <button className="workdir-btn" type="button" aria-label="打开最近工作目录"
        aria-expanded={menuOpen} aria-controls="workdirMenu"
        onClick={() => setMenuOpen((v) => !v)}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="m6 9 6 6 6-6" /></svg>
      </button>
      <button className="workdir-btn" type="button" aria-label="选择工作文件夹" onClick={onBrowse}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M3 7.5h7l2 2h9v9H3z" /><path d="M3 7.5V5h7l2 2.5" /></svg>
      </button>
      {menuOpen && (
        <div className="workdir-menu show" id="workdirMenu" role="menu">
          <div className="workdir-menu-title">最近与常用目录</div>
          {recent.length === 0 && <div className="workdir-option" style={{ color: 'var(--muted)', cursor: 'default' }}>暂无历史记录</div>}
          {recent.slice(0, 5).map((p) => (
            <button key={p} className="workdir-option" type="button" role="menuitem"
              onClick={() => { onChange(p); setMenuOpen(false); }}>{p}</button>
          ))}
        </div>
      )}
    </div>
  );
}
