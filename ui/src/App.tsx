import { useState } from 'react';
import { Rail, type View } from './Rail';
import { TopBar } from './TopBar';

const VIEW_TITLES: Record<View, string> = { launcher: '快速启动', tools: '工具库', sessions: '终端会话', settings: '设置' };

export default function App() {
  const [view, setView] = useState<View>('launcher');
  const termHidden = view === 'tools' || view === 'settings';
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
      <section className="terminal">
        <div className="term-tabs" id="termTabs" />
        <div className="term-body" id="termBody" />
      </section>
    </div>
  );
}
