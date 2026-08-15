import { useCallback, useEffect, useState } from 'react';
import { bridge } from './bridge';
import { Rail, type View } from './Rail';
import { TopBar } from './TopBar';
import { TerminalPanel } from './TerminalPanel';
import type { TerminalSessionInfo } from './types';

const VIEW_TITLES: Record<View, string> = { launcher: '快速启动', tools: '工具库', sessions: '终端会话', settings: '设置' };

export default function App() {
  const [view, setView] = useState<View>('launcher');
  const [sessions, setSessions] = useState<TerminalSessionInfo[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const termHidden = view === 'tools' || view === 'settings';

  const refreshSessions = useCallback(async () => {
    setSessions(await bridge.request<TerminalSessionInfo[]>('sessions.list'));
  }, []);

  useEffect(() => { refreshSessions(); }, [refreshSessions]);

  useEffect(() => bridge.on('sessions.changed', () => { refreshSessions(); }), [refreshSessions]);

  useEffect(() => {
    // activeId 为空或已失效（如关闭了当前激活标签）时，自动选中第一个会话
    if (sessions.length > 0 && !sessions.some((s) => s.sessionId === activeSessionId))
      setActiveSessionId(sessions[0].sessionId);
  }, [sessions, activeSessionId]);

  const handleNewShell = useCallback(async () => {
    try {
      const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.createShell', { cols: 120, rows: 30 });
      setActiveSessionId(sessionId);
      await refreshSessions();
    } catch (e) {
      console.error('新建会话失败', e); // Toast 在任务 16 接入
    }
  }, [refreshSessions]);

  const handleCloseSession = useCallback(async (id: string) => {
    await bridge.request('terminal.close', { sessionId: id }).catch(() => {});
    await refreshSessions();
  }, [refreshSessions]);
  return (
    <div className={`app${termHidden ? ' term-hidden' : ''}`}>
      <Rail view={view} onView={setView} version="" />
      <TopBar title={VIEW_TITLES[view]} userName="" onRefresh={() => { /* 任务 13 接真 */ }} />
      <main className="main" id="content">
        <section className="view-panel" data-view-panel="launcher" hidden={view !== 'launcher'}>
          <div className="main-head"><h1 className="title">快速启动</h1></div>
        </section>
        <section className="view-panel" data-view-panel="tools" hidden={view !== 'tools'}>
          <div className="main-head"><h1 className="title">工具库</h1></div>
        </section>
        <section className="view-panel" data-view-panel="sessions" hidden={view !== 'sessions'}>
          <div className="main-head"><h1 className="title">终端会话</h1></div>
        </section>
        <section className="view-panel" data-view-panel="settings" hidden={view !== 'settings'}>
          <div className="main-head"><h1 className="title">设置</h1></div>
        </section>
      </main>
      <TerminalPanel sessions={sessions} activeId={activeSessionId}
        onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
    </div>
  );
}
