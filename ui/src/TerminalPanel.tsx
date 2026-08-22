import { useEffect, useRef, useState } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { bridge } from './bridge';
import { FileTreePanel } from './FileTreePanel';
import { FileViewerPanel, type ViewerTab } from './FileViewerPanel';
import { ImageViewerPanel } from './ImageViewerPanel';
import { ConfirmModal } from './ConfirmModal';
import { fileBadge } from './fileIcons';
import { readXtermTheme } from './appearance';
import type { AccentColor, ResolvedColorMode, TerminalSessionInfo } from './types';

// write/resize 的在途失败：session-gone 前缀（关标签瞬间的良性竞态，后端会话校验）静默忽略，其余打日志
function ignoreSessionGone(e: unknown) {
  const msg = e instanceof Error ? e.message : String(e);
  if (!msg.startsWith('session-gone')) console.error(e);
}

// WebView2 首次布局时常是 0 高：FitAddon 读到 0/auto 会保持默认 24 行，选项卡下面只画出一小截。
function fitSession(sessionId: string, term: Terminal, fit: FitAddon, container: HTMLElement) {
  if (container.offsetParent === null) return;
  if (container.clientWidth < 20 || container.clientHeight < 20) return;
  try { fit.fit(); } catch { return; }
  bridge.request('terminal.resize', { sessionId, cols: term.cols, rows: term.rows }).catch(ignoreSessionGone);
}

function scheduleFit(sessionId: string, term: Terminal, fit: FitAddon, container: HTMLElement) {
  const run = () => fitSession(sessionId, term, fit, container);
  run();
  requestAnimationFrame(() => { run(); requestAnimationFrame(run); });
  setTimeout(run, 50);
}

// —— 文件查看 tab（与终端会话 tab 混排在同一 tab 栏）——
// Windows 路径大小写不敏感，去重与激活比较统一用规范化小写键
const fileKey = (p: string) => p.replace(/[\\/]+$/, '').toLowerCase();

const fileName = (p: string) => {
  const n = p.replace(/[\\/]+$/, '');
  const s = Math.max(n.lastIndexOf('\\'), n.lastIndexOf('/'));
  return s >= 0 ? n.slice(s + 1) : n;
};

// 文件 tab 按 kind 区分查看器：图片扩展名走 ImageViewerPanel，其余走文本查看器
type FileTabKind = 'text' | 'image';
type FileTab = ViewerTab & { kind: FileTabKind };

const IMAGE_EXT = new Set(['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp', 'ico', 'svg']);

function tabKindOf(path: string): FileTabKind {
  const name = fileName(path);
  const dot = name.lastIndexOf('.');
  if (dot <= 0) return 'text';
  return IMAGE_EXT.has(name.slice(dot + 1).toLowerCase()) ? 'image' : 'text';
}

export function TerminalPanel({ sessions, activeId, visible, workdir, colorMode, accentColor, onError, onInfo, onActivate, onNewSession, onCloseSession }: {
  sessions: TerminalSessionInfo[];
  activeId: string | null;
  visible: boolean;
  workdir: string | null;
  colorMode: ResolvedColorMode;
  accentColor: AccentColor;
  onError: (msg: string) => void;
  onInfo?: (msg: string) => void;
  onActivate: (id: string) => void;
  onNewSession: () => void;
  onCloseSession: (id: string) => void;
}) {
  const terms = useRef(new Map<string, { term: Terminal; fit: FitAddon }>());
  const containers = useRef(new Map<string, HTMLDivElement>());
  const observers = useRef(new Map<string, ResizeObserver>());
  // 早到分块缓冲：createShell 响应→refreshSessions 往返→effect 建实例之间存在窗口，
  // 此期间到达的 terminal.data 先缓存，实例创建后按序 flush，避免丢失首块输出。
  const pendingChunks = useRef(new Map<string, string[]>());

  // 文件查看 tab：activeFilePath 非空时查看器在前台（终端 hidden 但保持挂载，切回不丢输出）；
  // null 表示回到当前激活的会话 tab
  const [fileTabs, setFileTabs] = useState<FileTab[]>([]);
  const [activeFilePath, setActiveFilePath] = useState<string | null>(null);
  const [dirtyFiles, setDirtyFiles] = useState<Set<string>>(() => new Set());
  const [pendingClose, setPendingClose] = useState<string | null>(null);

  const openFile = (path: string) => {
    if (!workdir) return;
    setFileTabs((prev) => prev.some((t) => fileKey(t.path) === fileKey(path))
      ? prev
      : [...prev, { path, root: workdir, kind: tabKindOf(path) }]);
    setActiveFilePath(path);
  };

  const closeFileTab = (path: string) => {
    const key = fileKey(path);
    setFileTabs((prev) => prev.filter((t) => fileKey(t.path) !== key));
    setActiveFilePath((cur) => (cur != null && fileKey(cur) === key) ? null : cur);
    setDirtyFiles((prev) => {
      if (!prev.has(key)) return prev;
      const next = new Set(prev);
      next.delete(key);
      return next;
    });
  };

  const requestCloseFile = (path: string) => {
    if (dirtyFiles.has(fileKey(path))) setPendingClose(path);
    else closeFileTab(path);
  };

  const onDirtyChange = (path: string, dirty: boolean) => {
    const key = fileKey(path);
    setDirtyFiles((prev) => {
      if (prev.has(key) === dirty) return prev;
      const next = new Set(prev);
      if (dirty) next.add(key);
      else next.delete(key);
      return next;
    });
  };

  // 当前激活的文件 tab（决定哪个查看器在前台）
  const activeTab = activeFilePath == null
    ? undefined
    : fileTabs.find((t) => fileKey(t.path) === fileKey(activeFilePath));

  useEffect(() => bridge.on('terminal.data', ({ sessionId, chunk }: any) => {
    const entry = terms.current.get(sessionId);
    if (entry) { entry.term.write(chunk); return; }
    const buf = pendingChunks.current.get(sessionId);
    if (buf) buf.push(chunk);
    else pendingChunks.current.set(sessionId, [chunk]);
  }), []);

  useEffect(() => {
    for (const [id, entry] of terms.current)
      if (!sessions.some((s) => s.sessionId === id)) {
        entry.term.dispose();
        observers.current.get(id)?.disconnect();
        observers.current.delete(id);
        terms.current.delete(id);
        containers.current.delete(id);
        pendingChunks.current.delete(id);
      }
    for (const session of sessions) {
      const id = session.sessionId;
      if (terms.current.has(id) || !containers.current.has(id)) continue;
      const container = containers.current.get(id)!;
      const term = new Terminal({
        fontSize: 13,
        cursorBlink: true,
        theme: readXtermTheme(),
        // 与 --font-mono 同步：西文走 Cascadia/Consolas，缺字形时回退微软雅黑，避免 monospace→新宋体
        fontFamily: getComputedStyle(document.documentElement)
          .getPropertyValue('--font-mono').trim()
          || "ui-monospace, 'Cascadia Mono', 'Cascadia Code', Consolas, 'Microsoft YaHei UI', '微软雅黑', monospace",
      });
      const fit = new FitAddon();
      term.loadAddon(fit);
      term.open(container);
      scheduleFit(id, term, fit, container);
      term.onData((data) => bridge.request('terminal.write', { sessionId: id, data }).catch(ignoreSessionGone));
      const observer = new ResizeObserver(() => fitSession(id, term, fit, container));
      observer.observe(container);
      const buffered = pendingChunks.current.get(id);
      if (buffered) {
        for (const chunk of buffered) term.write(chunk);
        pendingChunks.current.delete(id);
      }
      terms.current.set(id, { term, fit });
      observers.current.set(id, observer);
    }
  }, [sessions]);

  useEffect(() => {
    const theme = readXtermTheme();
    for (const entry of terms.current.values()) entry.term.options.theme = theme;
  }, [colorMode, accentColor]);

  useEffect(() => {
    if (!visible || activeFilePath || !activeId) return;
    const entry = terms.current.get(activeId);
    const container = containers.current.get(activeId);
    if (!entry || !container) return;
    scheduleFit(activeId, entry.term, entry.fit, container);
  }, [visible, activeId, sessions, activeFilePath]);

  return (
    <section className="terminal">
      <FileTreePanel root={workdir} onError={onError} onInfo={onInfo} onOpenFile={openFile} />
      <div className="term-main">
        <div className="term-tabs" id="termTabs">
          {sessions.map((s) => (
            <button key={s.sessionId}
              className={`term-tab${s.sessionId === activeId && !activeFilePath ? ' active' : ''}`}
              onClick={() => { setActiveFilePath(null); onActivate(s.sessionId); }}>
              <span className={`status-dot${s.running ? '' : ' exited'}`} />{s.title}
              <span className="close" role="button" aria-label="关闭会话"
                onClick={(e) => { e.stopPropagation(); onCloseSession(s.sessionId); }}>×</span>
            </button>
          ))}
          {fileTabs.map((t) => {
            const name = fileName(t.path);
            const dot = name.lastIndexOf('.');
            const badge = fileBadge(name, false, dot > 0 ? name.slice(dot + 1) : '');
            return (
              <button key={fileKey(t.path)} title={t.path}
                className={`term-tab file-tab${activeFilePath != null && fileKey(activeFilePath) === fileKey(t.path) ? ' active' : ''}`}
                onClick={() => setActiveFilePath(t.path)}>
                {badge.kind === 'file' && (
                  <span className="file-badge" style={{ background: badge.bg, color: badge.fg }}>{badge.label}</span>
                )}
                <span className="term-tab-label">{name}</span>
                {dirtyFiles.has(fileKey(t.path)) && <span className="dirty-dot" title="未保存" />}
                <span className="close" role="button" aria-label="关闭文件"
                  onClick={(e) => { e.stopPropagation(); requestCloseFile(t.path); }}>×</span>
              </button>
            );
          })}
          <button className="icon-btn term-add" id="newTabBtn" title="新建终端标签" onClick={onNewSession}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M12 5v14M5 12h14" /></svg>
          </button>
        </div>
        {sessions.length === 0 && fileTabs.length === 0 && (
          <div className="term-empty">
            <p>还没有会话。从快速启动打开工具，或新建空白会话。</p>
            <button className="btn" onClick={onNewSession}>新建空白会话</button>
          </div>
        )}
        {sessions.map((s) => (
          <div key={s.sessionId}
            ref={(el) => { if (el) containers.current.set(s.sessionId, el); else containers.current.delete(s.sessionId); }}
            className="term-body" hidden={s.sessionId !== activeId || activeFilePath != null} />
        ))}
        {fileTabs.length > 0 && (
          <>
            <FileViewerPanel
              tabs={fileTabs.filter((t) => t.kind === 'text')}
              activePath={activeTab?.kind === 'text' ? activeFilePath : null}
              colorMode={colorMode}
              onClose={requestCloseFile}
              onDirtyChange={onDirtyChange}
            />
            <ImageViewerPanel
              tabs={fileTabs.filter((t) => t.kind === 'image')}
              activePath={activeTab?.kind === 'image' ? activeFilePath : null}
              onClose={closeFileTab}
            />
          </>
        )}
      </div>
      <ConfirmModal
        open={pendingClose != null}
        title="未保存的更改"
        subtitle={pendingClose ? `${fileName(pendingClose)} 有未保存的更改，关闭将丢失。` : undefined}
        confirmLabel="放弃更改"
        danger
        onCancel={() => setPendingClose(null)}
        onConfirm={() => {
          if (pendingClose) closeFileTab(pendingClose);
          setPendingClose(null);
        }}
      />
    </section>
  );
}
