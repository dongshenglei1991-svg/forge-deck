import { useEffect, useRef } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { bridge } from './bridge';
import { FileTreePanel } from './FileTreePanel';
import type { TerminalSessionInfo } from './types';

// 若 xterm canvas 渲染不支持 oklch（表现为黑底/默认色），换成十六进制近似值：
// background '#0d1211'、foreground '#b8c4bf'、cursor '#8fe3b0'、cursorAccent '#0d1211'
const THEME = {
  background: 'oklch(13% 0.02 170)',
  foreground: 'oklch(78% 0.02 170)',
  cursor: 'oklch(78% 0.15 155)',
  cursorAccent: 'oklch(13% 0.02 170)',
  selectionBackground: 'rgba(140, 255, 190, 0.25)',
};

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

export function TerminalPanel({ sessions, activeId, visible, workdir, onError, onInfo, onActivate, onNewSession, onCloseSession }: {
  sessions: TerminalSessionInfo[];
  activeId: string | null;
  visible: boolean;
  workdir: string | null;
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
        theme: THEME,
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
    if (!visible || !activeId) return;
    const entry = terms.current.get(activeId);
    const container = containers.current.get(activeId);
    if (!entry || !container) return;
    scheduleFit(activeId, entry.term, entry.fit, container);
  }, [visible, activeId, sessions]);

  return (
    <section className="terminal">
      <FileTreePanel root={workdir} onError={onError} onInfo={onInfo} />
      <div className="term-main">
        <div className="term-tabs" id="termTabs">
          {sessions.map((s) => (
            <button key={s.sessionId} className={`term-tab${s.sessionId === activeId ? ' active' : ''}`}
              onClick={() => onActivate(s.sessionId)}>
              <span className={`status-dot${s.running ? '' : ' exited'}`} />{s.title}
              <span className="close" role="button" aria-label="关闭会话"
                onClick={(e) => { e.stopPropagation(); onCloseSession(s.sessionId); }}>×</span>
            </button>
          ))}
          <button className="icon-btn term-add" id="newTabBtn" title="新建终端标签" onClick={onNewSession}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M12 5v14M5 12h14" /></svg>
          </button>
        </div>
        {sessions.length === 0 && (
          <div className="term-empty">
            <p>还没有会话。从快速启动打开工具，或新建空白会话。</p>
            <button className="btn" onClick={onNewSession}>新建空白会话</button>
          </div>
        )}
        {sessions.map((s) => (
          <div key={s.sessionId}
            ref={(el) => { if (el) containers.current.set(s.sessionId, el); else containers.current.delete(s.sessionId); }}
            className="term-body" hidden={s.sessionId !== activeId} />
        ))}
      </div>
    </section>
  );
}
