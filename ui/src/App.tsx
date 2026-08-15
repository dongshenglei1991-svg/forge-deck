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
    const list = await bridge.request<TerminalSessionInfo[]>('sessions.list');
    setSessions(list);
    // 数据到达期一并校正激活：cur 仍有效则保留（新建标签的显式 set 不被旧列表回退），失效则选剩余首个，全关归 null
    setActiveSessionId((cur) => (cur && list.some((s) => s.sessionId === cur) ? cur : list[0]?.sessionId ?? null));
  }, []);

  useEffect(() => { refreshSessions(); }, [refreshSessions]);

  useEffect(() => bridge.on('sessions.changed', () => { refreshSessions(); }), [refreshSessions]);

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
