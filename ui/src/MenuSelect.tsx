import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

export function MenuSelect({ value, options, onChange, ariaLabel }: {
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
  ariaLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [rect, setRect] = useState<DOMRect | null>(null);
  const current = options.find((o) => o.value === value)?.label ?? value;

  useEffect(() => {
    if (!open) return;
    const update = () => setRect(wrapRef.current?.getBoundingClientRect() ?? null);
    update();
    const onClick = (e: MouseEvent) => {
      const t = e.target as Node;
      if (wrapRef.current?.contains(t) || menuRef.current?.contains(t)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
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
  }, [open]);

  return (
    <div className="menu-select" ref={wrapRef}>
      <button type="button" className="input menu-select-trigger" aria-label={ariaLabel}
        aria-expanded={open} aria-haspopup="listbox"
        onClick={() => setOpen((v) => !v)}>
        <span className="menu-select-label">{current}</span>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden>
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open && rect && createPortal(
        <div className="menu-popup menu-popup-fixed" ref={menuRef} role="listbox" aria-label={ariaLabel}
          style={{ top: rect.bottom + 6, left: rect.left, width: rect.width }}>
          {options.map((o) => (
            <button key={o.value} type="button" role="option" aria-selected={o.value === value}
              className={`menu-option${o.value === value ? ' active' : ''}`}
              onClick={() => { onChange(o.value); setOpen(false); }}>
              {o.label}
            </button>
          ))}
        </div>,
        document.body,
      )}
    </div>
  );
}
