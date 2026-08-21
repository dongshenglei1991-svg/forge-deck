import { useEffect, useState } from 'react';
import { Modal } from './Modal';

export function ClosePromptModal({ open, onDismiss, onMinimize, onExit }: {
  open: boolean;
  onDismiss: () => void;
  onMinimize: (remember: boolean) => void;
  onExit: (remember: boolean) => void;
}) {
  const [remember, setRemember] = useState(false);

  useEffect(() => {
    if (open) setRemember(false);
  }, [open]);

  return (
    <Modal open={open} onClose={onDismiss} title="关闭 ForgeDeck？" subtitle="可以把窗口藏到托盘，会话继续跑。">
      <label className="radio-row">
        <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
        以后不再提示
      </label>
      <div className="modal-foot">
        <button className="btn" type="button" onClick={() => onExit(remember)}>退出应用</button>
        <button className="btn primary" type="button" onClick={() => onMinimize(remember)}>最小化到托盘</button>
      </div>
    </Modal>
  );
}
