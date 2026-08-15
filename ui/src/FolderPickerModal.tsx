import { useEffect, useMemo, useState } from 'react';
import { Modal } from './Modal';
import { baseName } from './lib/format';
import type { CommonDir } from './types';

export function FolderPickerModal({ open, initialValue, commonDirs, workdirs, onConfirm, onClose }: {
  open: boolean; initialValue: string; commonDirs: CommonDir[]; workdirs: string[];
  onConfirm: (path: string) => void; onClose: () => void;
}) {
  const [path, setPath] = useState(initialValue);
  const [active, setActive] = useState(initialValue);

  useEffect(() => { if (open) { setPath(initialValue); setActive(initialValue); } }, [open, initialValue]);

  const entries = useMemo(() => {
    const seen = new Set<string>();
    const list: { name: string; path: string }[] = [];
    for (const dir of [...workdirs, ...commonDirs.map((d) => d.path)]) {
      const key = dir.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      const named = commonDirs.find((d) => d.path === dir);
      list.push({ name: named?.name ?? baseName(dir), path: dir });
    }
    return list.slice(0, 12);
  }, [workdirs, commonDirs]);

  return (
    <Modal open={open} onClose={onClose} wide title="选择工作文件夹" subtitle="选择后将以完整 Windows 路径写入启动配置。">
      <div className="picker-path">
        <input className="input mono" aria-label="文件夹路径" value={path} onChange={(e) => setPath(e.target.value)} />
      </div>
      <div className="section-label">常用位置</div>
      <div className="picker-list">
        {entries.map((entry) => (
          <button key={entry.path} type="button" data-path={entry.path}
            className={`picker-folder${entry.path === active ? ' active' : ''}`}
            onClick={() => { setActive(entry.path); setPath(entry.path); }}>
            {entry.name}<small>{entry.path}</small>
          </button>
        ))}
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>取消</button>
        <button className="btn primary" onClick={() => path.trim() && onConfirm(path.trim())}>选择此文件夹</button>
      </div>
    </Modal>
  );
}
