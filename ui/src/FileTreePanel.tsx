import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import { fileBadge } from './fileIcons';
import type { FsEntry, FsListResult } from './types';

type Layer =
  | { kind: 'loading' }
  | { kind: 'empty' }
  | { kind: 'entries'; items: FsEntry[] }
  | { kind: 'error'; missing: boolean };

function folderName(root: string) {
  const trimmed = root.replace(/[\\/]+$/, '');
  const parts = trimmed.split(/[\\/]/);
  return parts[parts.length - 1] || trimmed;
}

function isMissingError(msg: string) {
  return msg.includes('not_found') || msg.includes('目录不存在');
}

export function FileTreePanel({ root, onError }: { root: string | null; onError: (msg: string) => void }) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [layers, setLayers] = useState<Map<string, Layer>>(() => new Map());

  const load = useCallback(async (path: string, rootPath: string, keepOnFail: boolean) => {
    setLayers((prev) => {
      const cur = prev.get(path);
      if (keepOnFail && cur?.kind === 'entries') return prev;
      const next = new Map(prev);
      next.set(path, { kind: 'loading' });
      return next;
    });
    try {
      const r = await bridge.request<FsListResult>('fs.list', { path, root: rootPath });
      setLayers((prev) => {
        const next = new Map(prev);
        next.set(path, r.entries.length === 0 ? { kind: 'empty' } : { kind: 'entries', items: r.entries });
        return next;
      });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      if (keepOnFail) onError(msg);
      setLayers((prev) => {
        const cur = prev.get(path);
        if (keepOnFail && cur?.kind === 'entries') return prev;
        const next = new Map(prev);
        next.set(path, { kind: 'error', missing: isMissingError(msg) });
        return next;
      });
    }
  }, [onError]);
  const loadRef = useRef(load);
  loadRef.current = load;

  // 只在激活会话 workdir 变化时换根；onError/load 引用变化不得清空展开态
  useEffect(() => {
    setExpanded(new Set());
    setLayers(new Map());
    if (root) void loadRef.current(root, root, false);
  }, [root]);

  const toggle = (entry: FsEntry) => {
    if (!root || !entry.isDirectory) return;
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(entry.path)) {
        next.delete(entry.path);
        return next;
      }
      next.add(entry.path);
      return next;
    });
    setLayers((prev) => {
      if (!root || prev.has(entry.path)) return prev;
      void load(entry.path, root, false);
      return prev;
    });
  };

  const refresh = () => {
    if (!root) return;
    void load(root, root, true);
    for (const p of expanded) void load(p, root, true);
  };

  return (
    <aside className="file-tree">
      <div className="file-tree-head">
        <span className="file-tree-label">工作区</span>
        {root && <span className="file-tree-name" title={root}>{folderName(root)}</span>}
        {root && (
          <button type="button" className="icon-btn" title="刷新" onClick={refresh}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
              <path d="M21 12a9 9 0 1 1-2.6-6.3M21 3v6h-6" />
            </svg>
          </button>
        )}
      </div>
      <div className="file-tree-body">
        {!root && <div className="file-tree-msg">还没有会话</div>}
        {root && <LayerView layer={layers.get(root)} depth={0} expanded={expanded} layers={layers} onToggle={toggle} />}
      </div>
    </aside>
  );
}

function LayerView({ layer, depth, expanded, layers, onToggle }: {
  layer: Layer | undefined; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; onToggle: (e: FsEntry) => void;
}) {
  if (!layer || layer.kind === 'loading') return <div className="file-tree-msg">读取中…</div>;
  if (layer.kind === 'empty') return <div className="file-tree-msg">空目录</div>;
  if (layer.kind === 'error') return <div className="file-tree-msg">{layer.missing ? '目录不存在' : '无法读取'}</div>;
  return (
    <>
      {layer.items.map((entry) => (
        <TreeNode key={entry.path} entry={entry} depth={depth} expanded={expanded} layers={layers} onToggle={onToggle} />
      ))}
    </>
  );
}

function TreeNode({ entry, depth, expanded, layers, onToggle }: {
  entry: FsEntry; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; onToggle: (e: FsEntry) => void;
}) {
  const open = entry.isDirectory && expanded.has(entry.path);
  const badge = fileBadge(entry.name, entry.isDirectory, entry.extension);
  return (
    <>
      <div
        className={`file-tree-row${entry.isDirectory ? ' dir' : ''}`}
        style={{ paddingLeft: 8 + depth * 12 }}
        onClick={() => onToggle(entry)}
      >
        <span className={`file-chevron${open ? ' open' : ''}${entry.isDirectory ? '' : ' hidden'}`}>▸</span>
        {badge.kind === 'folder' ? (
          <svg className="file-folder" viewBox="0 0 16 16" fill="none" aria-hidden>
            <path d="M2 4.2h4.1L7.4 5.8H14V13H2z" stroke="currentColor" strokeWidth="1.3" />
            <path d="M2 4.2V3h4.1L7 4.2" stroke="currentColor" strokeWidth="1.3" />
          </svg>
        ) : (
          <span className="file-badge" style={{ background: badge.bg, color: badge.fg }}>{badge.label}</span>
        )}
        <span className="file-tree-item">{entry.name}</span>
      </div>
      {open && (
        <LayerView layer={layers.get(entry.path)} depth={depth + 1} expanded={expanded} layers={layers} onToggle={onToggle} />
      )}
    </>
  );
}
