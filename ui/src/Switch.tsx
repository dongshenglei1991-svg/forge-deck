export function Switch({ on, label, onToggle }: { on: boolean; label: string; onToggle: () => void }) {
  return (
    <div className="switch-row">
      <span>{label}</span>
      <button className={`switch${on ? ' on' : ''}`} aria-label={label} onClick={onToggle}><i /></button>
    </div>
  );
}
