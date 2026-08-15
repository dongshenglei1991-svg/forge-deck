import { useEffect, useState, type ReactNode } from 'react';

export function Modal({ open, onClose, title, subtitle, wide, children }: {
  open: boolean; onClose: () => void; title: string; subtitle?: string; wide?: boolean; children: ReactNode;
}) {
  const [closing, setClosing] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') requestClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  });

  const requestClose = () => {
    if (!open || closing) return;
    setClosing(true);
    setTimeout(() => { setClosing(false); onClose(); }, 140);
  };

  if (!open && !closing) return null;
  return (
    <div className={`modal-wrap${open ? ' show' : ''}${closing ? ' closing' : ''}`}
      role="dialog" aria-modal="true" onClick={(e) => { if (e.target === e.currentTarget) requestClose(); }}>
      <div className={`modal${wide ? ' picker-modal' : ''}`}>
        <div className="modal-head">
          <div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>
          <button className="icon-btn" aria-label="关闭" onClick={requestClose}>×</button>
        </div>
        {children}
      </div>
    </div>
  );
}
