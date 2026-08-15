import { useEffect, useRef } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { bridge } from './bridge';
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

export function TerminalPanel({ sessions, activeId, onActivate, onNewSession, onCloseSession }: {
  sessions: TerminalSessionInfo[];
  activeId: string | null;
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
        fontSize: 11,
        cursorBlink: true,
        theme: THEME,
        fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
      });
      const fit = new FitAddon();
      term.loadAddon(fit);
      term.open(container);
      try { fit.fit(); } catch { /* 容器尺寸为 0 时忽略 */ }
      bridge.request('terminal.resize', { sessionId: id, cols: term.cols, rows: term.rows }).catch(() => {});
      term.onData((data) => bridge.request('terminal.write', { sessionId: id, data }).catch(() => {
        // session-gone：关标签瞬间的在途写入是良性竞态，静默忽略
      }));
      const observer = new ResizeObserver(() => {
        if (container.offsetParent === null) return;
        try { fit.fit(); } catch { return; }
        bridge.request('terminal.resize', { sessionId: id, cols: term.cols, rows: term.rows }).catch(() => {});
      });
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
    const entry = activeId ? terms.current.get(activeId) : null;
    if (entry && entry.fit)
      requestAnimationFrame(() => { try { entry.fit.fit(); } catch { /* 忽略 */ } });
  }, [activeId, sessions]);

  return (
    <section className="terminal">
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
      {sessions.map((s) => (
        <div key={s.sessionId}
          ref={(el) => { if (el) containers.current.set(s.sessionId, el); else containers.current.delete(s.sessionId); }}
          className="term-body" style={{ display: s.sessionId === activeId ? 'block' : 'none' }} />
      ))}
    </section>
  );
}
