import type { ReactNode } from 'react';
import type { AppInfo, TerminalSessionInfo, ToolListItem } from './types';
import { relativeTime } from './lib/format';
import { ToolListPanel } from './ToolListPanel';

export function LauncherView({ tools, scanning, selectedToolId, sessions, appInfo,
  onSelectTool, onRescan, onAddTool, configPanel }: {
  tools: ToolListItem[]; scanning: boolean; selectedToolId: string | null;
  sessions: TerminalSessionInfo[]; appInfo: AppInfo | null;
  onSelectTool: (id: string) => void; onRescan: () => void;
  onAddTool: () => void; configPanel: ReactNode;
}) {
  const lastTool = appInfo?.lastUsed ? tools.find((t) => t.tool.id === appInfo.lastUsed!.toolId) : undefined;
  const running = sessions.filter((s) => s.running).length;
  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">LOCAL TOOLCHAIN / 01</p>
          <h1 className="title">准备好开始编码了吗？</h1>
          <p className="sub">选择一个工具，载入你的工作区，马上进入状态。</p>
        </div>
        <button className="btn primary" onClick={onAddTool}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M12 5v14M5 12h14" /></svg>
          手动添加工具
        </button>
      </div>
      <section className="overview">
        <div className="metric">
          <div className="label">最近使用</div>
          <div className="value">{lastTool?.tool.name ?? '—'}</div>
          <div className="hint">{appInfo?.lastUsed?.workdir || '尚未启动过工具'}{lastTool && <span className="ok"> · 就绪</span>}</div>
        </div>
        <div className="metric">
          <div className="label">已识别工具</div>
          <div className="value num">{tools.length} <span style={{ font: '11px var(--font-body)', color: 'var(--muted)' }}>个</span></div>
          <div className="hint">上次扫描 {relativeTime(appInfo?.lastScanAt)}</div>
        </div>
        <div className="metric">
          <div className="label">活跃会话</div>
          <div className="value num">{running} <span style={{ font: '11px var(--font-body)', color: 'var(--muted)' }}>个</span></div>
          <div className="hint">{running > 0 ? '内嵌终端运行中' : '暂无运行中会话'}</div>
        </div>
      </section>
      <div className="workspace">
        <ToolListPanel tools={tools} scanning={scanning} selectedToolId={selectedToolId}
          onSelect={onSelectTool} onRescan={onRescan} />
        {configPanel}
      </div>
    </>
  );
}
