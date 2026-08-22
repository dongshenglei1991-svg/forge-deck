import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { PhotoProvider, PhotoView } from 'react-photo-view';
import 'react-photo-view/dist/react-photo-view.css';
import { bridge } from './bridge';
import type { FsReadImageResult } from './types';

export interface ImageTab {
  path: string;
  root: string;
}

type TabState =
  | { kind: 'loading' }
  | { kind: 'ready'; url: string }
  | { kind: 'error'; message: string };

// Windows 路径大小写不敏感，去重与索引统一用规范化小写键
const tabKey = (p: string) => p.replace(/[\\/]+$/, '').toLowerCase();

const fileName = (p: string) => {
  const n = p.replace(/[\\/]+$/, '');
  const s = Math.max(n.lastIndexOf('\\'), n.lastIndexOf('/'));
  return s >= 0 ? n.slice(s + 1) : n;
};

function displayError(e: unknown) {
  const msg = e instanceof Error ? e.message : String(e);
  return msg.replace(/^(validation|not_found|io|internal):\s*/, '');
}

// 工具栏按钮（react-photo-view 无默认工具栏，缩放/旋转按此渲染；滚轮与双击缩放由库内建）
function pvButton(title: string, onClick: () => void, icon: ReactNode) {
  return (
    <button type="button" className="pv-btn" title={title} onClick={onClick}>
      {icon}
    </button>
  );
}

/**
 * 图片查看器：面板内静态适配预览，点击经 react-photo-view 打开全屏灯箱
 * （滚轮/双击缩放、工具栏缩放旋转、Esc 关闭）。单实例挂载管理多个图片 tab，
 * base64 data URL 按 tab 缓存，tab 关闭即释放。
 */
export function ImageViewerPanel({ tabs, activePath, onClose }: {
  tabs: ImageTab[];
  activePath: string | null;
  onClose: (path: string) => void;
}) {
  const cache = useRef(new Map<string, string>()); // key → data URL
  const inflight = useRef(new Set<string>());
  const tabsRef = useRef(tabs);
  tabsRef.current = tabs;

  const [states, setStates] = useState<Map<string, TabState>>(() => new Map());
  const [retries, setRetries] = useState(0);

  const load = useCallback(async (tab: ImageTab) => {
    const key = tabKey(tab.path);
    if (cache.current.has(key) || inflight.current.has(key)) return;
    inflight.current.add(key);
    setStates((prev) => {
      const next = new Map(prev);
      next.set(key, { kind: 'loading' });
      return next;
    });
    try {
      const r = await bridge.request<FsReadImageResult>('fs.readImage', { path: tab.path, root: tab.root });
      if (!tabsRef.current.some((t) => tabKey(t.path) === key)) return;
      const url = `data:${r.mime};base64,${r.data}`;
      cache.current.set(key, url);
      setStates((prev) => {
        const next = new Map(prev);
        next.set(key, { kind: 'ready', url });
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

  useEffect(() => {
    const alive = new Set(tabs.map((t) => tabKey(t.path)));
    for (const [key] of cache.current) {
      if (!alive.has(key)) {
        cache.current.delete(key);
        setStates((prev) => {
          const next = new Map(prev);
          next.delete(key);
          return next;
        });
      }
    }
    for (const tab of tabs) void load(tab);
  }, [tabs, retries, load]);

  const activeKey = activePath ? tabKey(activePath) : null;
  const activeState = activeKey ? states.get(activeKey) : undefined;
  const readyUrl = activeState?.kind === 'ready' ? activeState.url : null;

  return (
    <div className="term-body image-viewer" hidden={activePath == null}>
      {readyUrl && (
        // key 换 tab 时重建 Provider，灯箱状态（缩放/旋转）不跨图片残留
        <PhotoProvider
          key={activeKey ?? undefined}
          maskOpacity={0.92}
          toolbarRender={({ scale, rotate, onScale, onRotate }) => (
            <>
              {pvButton('放大', () => onScale(scale * 1.25),
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.3-4.3M11 8v6M8 11h6" /></svg>)}
              {pvButton('缩小', () => onScale(scale / 1.25),
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.3-4.3M8 11h6" /></svg>)}
              {pvButton('重置', () => { onScale(1); onRotate(0); },
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5" /></svg>)}
              {pvButton('向左旋转', () => onRotate(rotate - 90),
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M4 9a8 8 0 1 1 2.3 8.5M4 4v5h5" /></svg>)}
              {pvButton('向右旋转', () => onRotate(rotate + 90),
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M20 9A8 8 0 1 0 17.7 17.5M20 4v5h-5" /></svg>)}
            </>
          )}
        >
          <PhotoView src={readyUrl}>
            <img
              className="image-preview"
              src={readyUrl}
              alt={activePath ? fileName(activePath) : ''}
              title="点击放大：滚轮缩放，工具栏可旋转"
              draggable={false}
            />
          </PhotoView>
        </PhotoProvider>
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
