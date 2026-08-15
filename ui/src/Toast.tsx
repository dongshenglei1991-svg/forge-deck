export interface ToastItem { id: number; text: string; kind: 'info' | 'error' }

export function Toast({ items }: { items: ToastItem[] }) {
  if (items.length === 0) return null;
  return (
    <div className="toast-wrap">
      {items.map((t) => (
        <div key={t.id} className={`toast${t.kind === 'error' ? ' error' : ''}`}>{t.text}</div>
      ))}
    </div>
  );
}
