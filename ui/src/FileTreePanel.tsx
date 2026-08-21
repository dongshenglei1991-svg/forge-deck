import { useCallback, useEffect, useLayoutEffect, useRef, useState, type MouseEvent } from 'react';
import { bridge } from './bridge';
import { ConfirmModal } from './ConfirmModal';
import { fileBadge } from './fileIcons';
import type { FsEntry, FsListResult } from './types';

type Layer =
  | { kind: 'loading' }
  | { kind: 'empty' }
  | { kind: 'entries'; items: FsEntry[] }
  | { kind: 'error'; missing: boolean };

type MenuState = { x: number; y: number; entry: FsEntry };

function folderName(root: string) {
  const trimmed = root.replace(/[\\/]+$/, '');
  const parts = trimmed.split(/[\\/]/);
  return parts[parts.length - 1] || trimmed;
}

function isMissingError(msg: string) {
  return msg.includes('not_found') || msg.includes('目录不存在');
}

function keepLayerOnFail(cur: Layer | undefined) {
  return cur?.kind === 'entries' || cur?.kind === 'empty';
}

function samePath(a: string, b: string) {
  return a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();
}

function isUnder(full: string, dir: string) {
  const a = full.replace(/[\\/]+$/, '').toLowerCase();
  const b = dir.replace(/[\\/]+$/, '').toLowerCase();
  return a === b || a.startsWith(b + '\\') || a.startsWith(b + '/');
}

function parentDir(p: string) {
  const n = p.replace(/[\\/]+$/, '');
  const slash = Math.max(n.lastIndexOf('\\'), n.lastIndexOf('/'));
  if (slash < 0) return n;
  if (slash === 2 && n[1] === ':') return n.slice(0, 3);
  return n.slice(0, slash);
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.left = '-9999px';
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    ta.remove();
  }
}

export function FileTreePanel({ root, onError, onInfo }: {
  root: string | null;
  onError: (msg: string) => void;
  onInfo?: (msg: string) => void;
}) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [layers, setLayers] = useState<Map<string, Layer>>(() => new Map());
  const [menu, setMenu] = useState<MenuState | null>(null);
  const [pendingDelete, setPendingDelete] = useState<FsEntry | null>(null);
  const loadGen = useRef(0);
  const menuRef = useRef<HTMLDivElement>(null);

  const load = useCallback(async (path: string, rootPath: string, keepOnFail: boolean) => {
    const gen = loadGen.current;
    setLayers((prev) => {
      const cur = prev.get(path);
      if (keepOnFail && keepLayerOnFail(cur)) return prev;
      const next = new Map(prev);
      next.set(path, { kind: 'loading' });
      return next;
    });
    try {
      const r = await bridge.request<FsListResult>('fs.list', { path, root: rootPath });
      if (gen !== loadGen.current) return;
      setLayers((prev) => {
        const next = new Map(prev);
        next.set(path, r.entries.length === 0 ? { kind: 'empty' } : { kind: 'entries', items: r.entries });
        return next;
      });
    } catch (e: unknown) {
      if (gen !== loadGen.current) return;
      const msg = e instanceof Error ? e.message : String(e);
      if (keepOnFail) onError(msg);
      setLayers((prev) => {
        const cur = prev.get(path);
        if (keepOnFail && keepLayerOnFail(cur)) return prev;
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
    loadGen.current += 1;
    setExpanded(new Set());
    setLayers(new Map());
    setMenu(null);
    setPendingDelete(null);
    if (root) void loadRef.current(root, root, false);
  }, [root]);

  const toggle = (entry: FsEntry) => {
    if (!root || !entry.isDirectory) return;
    const willExpand = !expanded.has(entry.path);
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(entry.path)) next.delete(entry.path);
      else next.add(entry.path);
      return next;
    });
    if (!willExpand) return;
    const cur = layers.get(entry.path);
    // 已缓存成功或在途不重复请求；error / 未加载要重新 fs.list
    if (cur?.kind === 'entries' || cur?.kind === 'loading') return;
    void load(entry.path, root, false);
  };

  const refresh = () => {
    if (!root) return;
    void load(root, root, true);
    for (const p of expanded) void load(p, root, true);
  };

  const refreshDir = (dirPath: string) => {
    if (!root) return;
    void load(dirPath, root, true);
    for (const p of expanded) {
      if (p !== dirPath && isUnder(p, dirPath)) void load(p, root, true);
    }
  };

  const prune = (path: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      for (const p of next) if (isUnder(p, path)) next.delete(p);
      return next;
    });
    setLayers((prev) => {
      const next = new Map(prev);
      for (const k of next.keys()) if (isUnder(k, path)) next.delete(k);
      return next;
    });
  };

  const openMenu = (event: MouseEvent, entry: FsEntry) => {
    event.preventDefault();
    event.stopPropagation();
    setMenu({ x: event.clientX, y: event.clientY, entry });
  };

  useLayoutEffect(() => {
    if (!menu || !menuRef.current) return;
    const r = menuRef.current.getBoundingClientRect();
    let x = menu.x;
    let y = menu.y;
    if (x + r.width > window.innerWidth - 8) x = Math.max(8, window.innerWidth - r.width - 8);
    if (y + r.height > window.innerHeight - 8) y = Math.max(8, window.innerHeight - r.height - 8);
    if (x !== menu.x || y !== menu.y) setMenu({ ...menu, x, y });
  }, [menu]);

  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    window.addEventListener('mousedown', close);
    window.addEventListener('keydown', onKey);
    window.addEventListener('blur', close);
    return () => {
      window.removeEventListener('mousedown', close);
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('blur', close);
    };
  }, [menu]);

  const run = async (method: string, entry: FsEntry) => {
    if (!root) return;
    try {
      await bridge.request(method, { path: entry.path, root });
    } catch (e: unknown) {
      onError(e instanceof Error ? e.message : String(e));
    }
  };

  const handleCopy = async (text: string, ok: string) => {
    try {
      await copyText(text);
      onInfo?.(ok);
    } catch (e: unknown) {
      onError(e instanceof Error ? e.message : String(e));
    }
  };

  const confirmDelete = async () => {
    const entry = pendingDelete;
    setPendingDelete(null);
    if (!entry || !root) return;
    try {
      await bridge.request('fs.delete', { path: entry.path, root });
      prune(entry.path);
      const parentRaw = parentDir(entry.path) || root;
      const parent = samePath(parentRaw, root) ? root : parentRaw;
      setLayers((prev) => {
        const cur = prev.get(parent);
        if (cur?.kind !== 'entries') return prev;
        const items = cur.items.filter((e) => !samePath(e.path, entry.path));
        const next = new Map(prev);
        next.set(parent, items.length === 0 ? { kind: 'empty' } : { kind: 'entries', items });
        return next;
      });
      void load(parent, root, true);
      onInfo?.(`已删除「${entry.name}」`);
    } catch (e: unknown) {
      onError(e instanceof Error ? e.message : String(e));
    }
  };

  const isRootEntry = (entry: FsEntry) => !!root && samePath(entry.path, root);

  return (
    <aside className="file-tree" onContextMenu={(e) => e.preventDefault()}>
      <div className="file-tree-head">
        <span className="file-tree-label">工作区</span>
        {root && (
          <span
            className={`file-tree-name${menu && samePath(menu.entry.path, root) ? ' is-menu' : ''}`}
            title={root}
            onContextMenu={(e) => openMenu(e, { name: folderName(root), path: root, isDirectory: true, extension: '' })}
          >
            {folderName(root)}
          </span>
        )}
        {root && (
          <button type="button" className="icon-btn" title="刷新" onClick={refresh}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
              <path d="M21 12a9 9 0 1 1-2.6-6.3M21 3v6h-6" />
            </svg>
          </button>
        )}
      </div>
      <div className="file-tree-body" onScroll={() => setMenu(null)}>
        {!root && <div className="file-tree-msg">还没有会话</div>}
        {root && (
          <LayerView
            layer={layers.get(root)}
            depth={0}
            expanded={expanded}
            layers={layers}
            menuPath={menu?.entry.path ?? null}
            onToggle={toggle}
            onMenu={openMenu}
          />
        )}
      </div>
      {menu && (
        <div
          ref={menuRef}
          className="ctx-menu"
          role="menu"
          style={{ left: menu.x, top: menu.y }}
          onMouseDown={(e) => e.stopPropagation()}
        >
          {menu.entry.isDirectory && (
            <button type="button" role="menuitem" onClick={() => { refreshDir(menu.entry.path); setMenu(null); }}>
              刷新
            </button>
          )}
          {!menu.entry.isDirectory && (
            <button type="button" role="menuitem" onClick={() => { void run('fs.open', menu.entry); setMenu(null); }}>
              打开
            </button>
          )}
          <button type="button" role="menuitem" onClick={() => { void run('fs.openWithSystem', menu.entry); setMenu(null); }}>
            使用系统默认方式打开
          </button>
          <button type="button" role="menuitem" onClick={() => { void handleCopy(menu.entry.name, '已复制文件名'); setMenu(null); }}>
            复制文件名
          </button>
          <button type="button" role="menuitem" onClick={() => { void handleCopy(menu.entry.path, '已复制路径'); setMenu(null); }}>
            复制路径
          </button>
          {!isRootEntry(menu.entry) && (
            <>
              <div className="ctx-menu-sep" />
              <button
                type="button"
                role="menuitem"
                className="danger"
                onClick={() => { setPendingDelete(menu.entry); setMenu(null); }}
              >
                删除
              </button>
            </>
          )}
        </div>
      )}
      <ConfirmModal
        open={pendingDelete != null}
        title={pendingDelete
          ? `删除${pendingDelete.isDirectory ? '目录' : '文件'}「${pendingDelete.name}」？`
          : ''}
        subtitle={pendingDelete
          ? (pendingDelete.isDirectory
            ? `将永久删除该目录及其全部内容：${pendingDelete.path}`
            : `将永久删除：${pendingDelete.path}`)
          : undefined}
        confirmLabel="删除"
        danger
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => { void confirmDelete(); }}
      />
    </aside>
  );
}

function LayerView({ layer, depth, expanded, layers, menuPath, onToggle, onMenu }: {
  layer: Layer | undefined; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; menuPath: string | null;
  onToggle: (e: FsEntry) => void;
  onMenu: (e: MouseEvent, entry: FsEntry) => void;
}) {
  const pad = { paddingLeft: 8 + depth * 12 };
  if (!layer || layer.kind === 'loading') return <div className="file-tree-msg" style={pad}>读取中…</div>;
  if (layer.kind === 'empty') return <div className="file-tree-msg" style={pad}>空目录</div>;
  if (layer.kind === 'error') {
    const text = layer.missing && depth === 0 ? '目录不存在' : '无法读取';
    return <div className="file-tree-msg" style={pad}>{text}</div>;
  }
  return (
    <>
      {layer.items.map((entry) => (
        <TreeNode
          key={entry.path}
          entry={entry}
          depth={depth}
          expanded={expanded}
          layers={layers}
          menuPath={menuPath}
          onToggle={onToggle}
          onMenu={onMenu}
        />
      ))}
    </>
  );
}

function TreeNode({ entry, depth, expanded, layers, menuPath, onToggle, onMenu }: {
  entry: FsEntry; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; menuPath: string | null;
  onToggle: (e: FsEntry) => void;
  onMenu: (e: MouseEvent, entry: FsEntry) => void;
}) {
  const open = entry.isDirectory && expanded.has(entry.path);
  const badge = fileBadge(entry.name, entry.isDirectory, entry.extension);
  const active = menuPath != null && samePath(menuPath, entry.path);
  return (
    <>
      <div
        className={`file-tree-row${entry.isDirectory ? ' dir' : ''}${active ? ' is-menu' : ''}`}
        style={{ paddingLeft: 8 + depth * 12 }}
        onClick={() => onToggle(entry)}
        onContextMenu={(e) => onMenu(e, entry)}
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
        <LayerView
          layer={layers.get(entry.path)}
          depth={depth + 1}
          expanded={expanded}
          layers={layers}
          menuPath={menuPath}
          onToggle={onToggle}
          onMenu={onMenu}
        />
      )}
    </>
  );
}
