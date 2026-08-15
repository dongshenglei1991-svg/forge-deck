import { useState } from 'react';
import { Modal } from './Modal';

export function AddToolModal({ open, onClose, onConfirm }: {
  open: boolean; onClose: () => void;
  onConfirm: (name: string, exePath: string) => Promise<void>;
}) {
  const [name, setName] = useState('');
  const [path, setPath] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!name.trim() || !path.trim()) { setError('请填写工具名称与可执行文件路径'); return; }
    try {
      await onConfirm(name.trim(), path.trim());
      setName(''); setPath(''); setError(null);
    } catch (e: any) { setError(e.message); }
  };

  return (
    <Modal open={open} onClose={onClose} title="添加本地工具" subtitle="将未被自动识别的 CLI 工具加入启动列表。">
      <div className="field">
        <label htmlFor="newName">工具名称</label>
        <input className="input" id="newName" placeholder="例如：Gemini CLI" value={name}
          onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="field">
        <label htmlFor="newPath">可执行文件路径</label>
        <input className="input mono" id="newPath" placeholder="C:\\Program Files\\...\\tool.exe" value={path}
          onChange={(e) => setPath(e.target.value)} />
      </div>
      {error && <p style={{ color: '#e5484d', fontSize: 12, margin: '0 0 8px' }}>{error}</p>}
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>取消</button>
        <button className="btn primary" onClick={submit}>添加到工具库</button>
      </div>
    </Modal>
  );
}
