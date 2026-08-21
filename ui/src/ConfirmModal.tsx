import { Modal } from './Modal';

export function ConfirmModal({ open, title, subtitle, confirmLabel, cancelLabel, danger, onCancel, onConfirm }: {
  open: boolean;
  title: string;
  subtitle?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  return (
    <Modal open={open} onClose={onCancel} title={title} subtitle={subtitle}>
      <div className="modal-foot">
        <button className="btn" type="button" onClick={onCancel}>{cancelLabel ?? '取消'}</button>
        <button className={`btn primary${danger ? ' danger' : ''}`} type="button" onClick={onConfirm}>
          {confirmLabel ?? '确定'}
        </button>
      </div>
    </Modal>
  );
}
