import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import { monacoThemeName } from './appearance';
import { languageForPath, monaco } from './monacoSetup';
import type { ResolvedColorMode } from './types';
import type { FsReadResult, FsWriteResult } from './types';

/** 一个已打开的文件 tab：path 是显示与去重键，root 是打开时的工作目录（fs.read / fs.write 的路径守卫用） */
export interface ViewerTab {
  path: string;
  root: string;
}

type TabState =
  | { kind: 'loading' }
  | { kind: 'ready' }
  | { kind: 'error'; message: string };

// Windows 路径大小写不敏感，去重与索引统一用规范化小写键
const tabKey = (p: string) => p.replace(/[\\/]+$/, '').toLowerCase();

// 桥错误格式是 "code: 中文说明"，展示时剥掉前缀
function displayError(e: unknown) {
  const msg = e instanceof Error ? e.message : String(e);
  return msg.replace(/^(validation|not_found|io|internal):\s*/, '');
}

function encodingLabel(enc: string) {
  switch (enc) {
    case 'utf-8': return 'UTF-8';
    case 'utf-8bom': return 'UTF-8 BOM';
    case 'utf-16le': return 'UTF-16 LE';
    case 'utf-16be': return 'UTF-16 BE';
    case 'gbk': return 'GBK';
    default: return enc.toUpperCase();
  }
}

/**
 * 文本编辑器：单个 Monaco 实例 + 每文件一个 Model（切 tab 只换 model）。
 * 读取失败在 tab 内联展示错误与重试；保存失败写在底栏，不盖住编辑区。
 */
export function FileViewerPanel({ tabs, activePath, colorMode, onClose, onDirtyChange }: {
  tabs: ViewerTab[];
  activePath: string | null;
  colorMode: ResolvedColorMode;
  onClose: (path: string) => void;
  onDirtyChange?: (path: string, dirty: boolean) => void;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const colorModeRef = useRef(colorMode);
  colorModeRef.current = colorMode;
  const modelsRef = useRef(new Map<string, monaco.editor.ITextModel>());
  const encodingsRef = useRef(new Map<string, string>());
  const savedVersionRef = useRef(new Map<string, number>());
  const inflight = useRef(new Set<string>());
  const tabsRef = useRef(tabs);
  tabsRef.current = tabs;
  const activePathRef = useRef(activePath);
  activePathRef.current = activePath;
  const onDirtyChangeRef = useRef(onDirtyChange);
  onDirtyChangeRef.current = onDirtyChange;

  const [states, setStates] = useState<Map<string, TabState>>(() => new Map());
  const [retries, setRetries] = useState(0);
  const [dirty, setDirty] = useState<Set<string>>(() => new Set());
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const markDirty = useCallback((path: string, isDirty: boolean) => {
    const key = tabKey(path);
    setDirty((prev) => {
      if (prev.has(key) === isDirty) return prev;
      const next = new Set(prev);
      if (isDirty) next.add(key);
      else next.delete(key);
      return next;
    });
    onDirtyChangeRef.current?.(path, isDirty);
  }, []);

  const bindModel = useCallback((path: string, model: monaco.editor.ITextModel) => {
    savedVersionRef.current.set(tabKey(path), model.getAlternativeVersionId());
    model.onDidChangeContent(() => {
      const key = tabKey(path);
      const saved = savedVersionRef.current.get(key);
      markDirty(path, saved == null || model.getAlternativeVersionId() !== saved);
    });
  }, [markDirty]);

  useEffect(() => {
    if (!hostRef.current) return;
    const editor = monaco.editor.create(hostRef.current, {
      theme: monacoThemeName(colorModeRef.current),
      readOnly: false,
      minimap: { enabled: false },
      automaticLayout: true,
      fontSize: 13,
      lineHeight: 20,
      fontFamily: getComputedStyle(document.documentElement).getPropertyValue('--font-mono').trim() || undefined,
      scrollBeyondLastLine: false,
      wordWrap: 'off',
      overviewRulerLanes: 0,
      scrollbar: { verticalScrollbarSize: 10, horizontalScrollbarSize: 10, useShadows: false },
      stickyScroll: { enabled: false },
      quickSuggestions: false,
    });
    editorRef.current = editor;
    return () => {
      editor.dispose();
      editorRef.current = null;
    };
  }, []);

  useEffect(() => {
    monaco.editor.setTheme(monacoThemeName(colorMode));
  }, [colorMode]);

  const load = useCallback(async (tab: ViewerTab) => {
    const key = tabKey(tab.path);
    if (modelsRef.current.has(key) || inflight.current.has(key)) return;
    inflight.current.add(key);
    setStates((prev) => {
      const next = new Map(prev);
      next.set(key, { kind: 'loading' });
      return next;
    });
    try {
      const r = await bridge.request<FsReadResult>('fs.read', { path: tab.path, root: tab.root });
      if (!tabsRef.current.some((t) => tabKey(t.path) === key)) return;
      const uri = monaco.Uri.file(tab.path);
      const existing = monaco.editor.getModel(uri);
      const model = existing ?? monaco.editor.createModel(r.content, languageForPath(tab.path), uri);
      encodingsRef.current.set(key, r.encoding);
      modelsRef.current.set(key, model);
      if (!existing) {
        // setEOL 可能改 versionId，必须在记下“已保存版本”之前
        model.setEOL(r.content.includes('\r\n')
          ? monaco.editor.EndOfLineSequence.CRLF
          : monaco.editor.EndOfLineSequence.LF);
        bindModel(tab.path, model);
      }
      setStates((prev) => {
        const next = new Map(prev);
        next.set(key, { kind: 'ready' });
        return next;
      });
    } catch (e: unknown) {
      if (!tabsRef.current.some((t) => tabKey(t.path) === key)) return;
      setStates((prev) => {
        const next = new Map(prev);
        next.set(key, { kind: 'error', message: displayError(e) });
        return next;
      });
    } finally {
      inflight.current.delete(key);
    }
  }, [bindModel]);

  useEffect(() => {
    const alive = new Set(tabs.map((t) => tabKey(t.path)));
    for (const [key, model] of modelsRef.current) {
      if (alive.has(key)) continue;
      if (editorRef.current?.getModel() === model) editorRef.current.setModel(null);
      model.dispose();
      modelsRef.current.delete(key);
      encodingsRef.current.delete(key);
      savedVersionRef.current.delete(key);
    }
    setDirty((prev) => {
      let changed = false;
      const next = new Set(prev);
      for (const key of prev) {
        if (alive.has(key)) continue;
        next.delete(key);
        changed = true;
      }
      return changed ? next : prev;
    });
    for (const tab of tabs) void load(tab);
  }, [tabs, retries, load]);

  useEffect(() => {
    const editor = editorRef.current;
    if (!editor) return;
    const model = activePath ? modelsRef.current.get(tabKey(activePath)) ?? null : null;
    if (editor.getModel() !== model) editor.setModel(model);
    if (activePath) setSaveError(null);
  }, [activePath, states]);

  const save = useCallback(async () => {
    const path = activePathRef.current;
    if (!path) return;
    const key = tabKey(path);
    const tab = tabsRef.current.find((t) => tabKey(t.path) === key);
    const model = modelsRef.current.get(key);
    if (!tab || !model || !dirtyRef.current.has(key)) return;
    setSaving(true);
    setSaveError(null);
    try {
      await bridge.request<FsWriteResult>('fs.write', {
        path: tab.path,
        root: tab.root,
        content: model.getValue(),
        encoding: encodingsRef.current.get(key) ?? 'utf-8',
      });
      savedVersionRef.current.set(key, model.getAlternativeVersionId());
      markDirty(tab.path, false);
    } catch (e: unknown) {
      setSaveError(displayError(e));
    } finally {
      setSaving(false);
    }
  }, [markDirty]);

  const saveRef = useRef(save);
  saveRef.current = save;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 's') return;
      if (activePathRef.current == null) return;
      e.preventDefault();
      void saveRef.current();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const activeKey = activePath ? tabKey(activePath) : '';
  const activeState = activePath ? states.get(activeKey) : undefined;
  const isDirty = activePath ? dirty.has(activeKey) : false;
  const encoding = activePath ? encodingsRef.current.get(activeKey) : undefined;

  return (
    <div className="term-body file-viewer" hidden={activePath == null}>
      <div ref={hostRef} className="viewer-host" />
      {activePath && activeState?.kind === 'ready' && (
        <div className="viewer-bar">
          <span className="viewer-bar-meta">{encoding ? encodingLabel(encoding) : ''}</span>
          {saveError
            ? <span className="viewer-bar-error">{saveError}</span>
            : isDirty
              ? <span className="viewer-bar-dirty">未保存</span>
              : <span className="viewer-bar-ok">已保存</span>}
          <button className="btn small" type="button" disabled={!isDirty || saving} onClick={() => void save()}>
            {saving ? '保存中…' : '保存'}
          </button>
        </div>
      )}
      {activePath && activeState?.kind === 'loading' && (
        <div className="viewer-overlay"><p className="viewer-msg">读取中…</p></div>
      )}
      {activePath && activeState?.kind === 'error' && (
        <div className="viewer-overlay">
          <p className="viewer-msg">{activeState.message}</p>
          <div className="viewer-actions">
            <button className="btn small" onClick={() => setRetries((n) => n + 1)}>重试</button>
            <button className="btn small" onClick={() => onClose(activePath)}>关闭</button>
          </div>
        </div>
      )}
    </div>
  );
}
