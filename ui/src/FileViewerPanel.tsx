import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import { languageForPath, monaco } from './monacoSetup';
import type { FsReadResult } from './types';

/** 一个已打开的文件 tab：path 是显示与去重键，root 是打开时的工作目录（fs.read 的路径守卫用） */
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

/**
 * 文本查看器：单个 Monaco 编辑器实例 + 每文件一个 Model（切 tab 只换 model，不开新实例）。
 * 读取失败（超 1MB / 二进制 / 文件被删）在 tab 内联展示错误与重试，不弹 toast 打扰终端。
 */
export function FileViewerPanel({ tabs, activePath, onClose }: {
  tabs: ViewerTab[];
  activePath: string | null;
  onClose: (path: string) => void;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const modelsRef = useRef(new Map<string, monaco.editor.ITextModel>());
  const inflight = useRef(new Set<string>());
  const tabsRef = useRef(tabs);
  tabsRef.current = tabs;

  const [states, setStates] = useState<Map<string, TabState>>(() => new Map());
  const [retries, setRetries] = useState(0);

  useEffect(() => {
    if (!hostRef.current) return;
    const editor = monaco.editor.create(hostRef.current, {
      theme: 'forgedeck-dark',
      readOnly: true,
      minimap: { enabled: false },
      automaticLayout: true, // 内建 ResizeObserver：窗口缩放 / 分栏变化自动重排
      fontSize: 13,
      lineHeight: 20,
      // 与 xterm 一致：西文走 Cascadia/Consolas，缺字形回退微软雅黑
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
      // 等待期间 tab 可能已关闭，丢弃结果（model 不创建即无泄漏）
      if (!tabsRef.current.some((t) => tabKey(t.path) === key)) return;
      const uri = monaco.Uri.file(tab.path);
      // tab 关闭后立即重开同一路径时 model 可能尚存：复用而不是重复创建（重复 uri 会抛错）
      const model = monaco.editor.getModel(uri) ?? monaco.editor.createModel(r.content, languageForPath(tab.path), uri);
      modelsRef.current.set(key, model);
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
  }, []);

  // 新 tab 触发读取；已关闭 tab 释放 model（retries 变化 = 当前 tab 请求重试）
  useEffect(() => {
    const alive = new Set(tabs.map((t) => tabKey(t.path)));
    for (const [key, model] of modelsRef.current) {
      if (alive.has(key)) continue;
      if (editorRef.current?.getModel() === model) editorRef.current.setModel(null);
      model.dispose();
      modelsRef.current.delete(key);
    }
    for (const tab of tabs) void load(tab);
  }, [tabs, retries, load]);

  // 激活 tab 变化（或 model 读取完成）时挂上对应 model
  useEffect(() => {
    const editor = editorRef.current;
    if (!editor) return;
    const model = activePath ? modelsRef.current.get(tabKey(activePath)) ?? null : null;
    if (editor.getModel() !== model) editor.setModel(model);
  }, [activePath, states]);

  const activeState = activePath ? states.get(tabKey(activePath)) : undefined;

  return (
    <div className="term-body file-viewer" hidden={activePath == null}>
      <div ref={hostRef} className="viewer-host" />
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
