import { useCallback, useEffect, useState } from 'react';
import { bridge } from './bridge';
import { Rail, type View } from './Rail';
import { TopBar } from './TopBar';
import { LauncherView } from './LauncherView';
import { TerminalPanel } from './TerminalPanel';
import { AddToolModal } from './AddToolModal';
import type { AppInfo, LaunchProfile, SettingsInfo, TerminalSessionInfo, ToolListItem } from './types';

const VIEW_TITLES: Record<View, string> = { launcher: '快速启动', tools: '工具库', sessions: '终端会话', settings: '设置' };

export default function App() {
  const [view, setView] = useState<View>('launcher');
  const [tools, setTools] = useState<ToolListItem[]>([]);
  const [sessions, setSessions] = useState<TerminalSessionInfo[]>([]);
  const [settingsInfo, setSettingsInfo] = useState<SettingsInfo | null>(null);
  const [appInfo, setAppInfo] = useState<AppInfo | null>(null);
  // 值自任务 14（FolderPicker 读 workdirs、ConfigPanel 以 profile.id 作重置依赖）起被读取，暂以 _ 前缀豁免未用检查
  const [_workdirs, setWorkdirs] = useState<string[]>([]);
  const [selectedToolId, setSelectedToolId] = useState<string | null>(null);
  const [_profile, setProfile] = useState<LaunchProfile | null>(null);
  const [scanning, setScanning] = useState(false);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  const refreshSessions = useCallback(async () => {
    const list = await bridge.request<TerminalSessionInfo[]>('sessions.list');
    setSessions(list);
    // 数据到达期一并校正激活：cur 仍有效则保留（新建标签的显式 set 不被旧列表回退），失效则选剩余首个，全关归 null
    setActiveSessionId((cur) => (cur && list.some((s) => s.sessionId === cur) ? cur : list[0]?.sessionId ?? null));
  }, []);
  const refreshWorkdirs = useCallback(async () => {
    setWorkdirs(await bridge.request<string[]>('workdirs.list'));
  }, []);

  const selectTool = useCallback(async (toolId: string) => {
    setSelectedToolId(toolId);
    setProfile(await bridge.request<LaunchProfile>('profiles.get', { toolId }));
  }, []);

  // 启动主线（合并任务 12 的会话初始拉取与订阅，避免重复请求）：appInfo/settings → 按设置决定 rescan 或 list → 会话/工作目录 → 选中 preferred 工具
  useEffect(() => {
    let disposed = false;
    (async () => {
      const [info, si] = await Promise.all([
        bridge.request<AppInfo>('app.info'),
        bridge.request<SettingsInfo>('settings.get'),
      ]);
      if (disposed) return;
      setAppInfo(info);
      setSettingsInfo(si);
      let list: ToolListItem[];
      if (si.settings.autoScanOnStartup) {
        setScanning(true);
        try {
          list = await bridge.request<ToolListItem[]>('tools.rescan');
          if (!disposed) setAppInfo(await bridge.request<AppInfo>('app.info'));
        } finally { setScanning(false); } // 失败/卸载均复位扫描态
      } else {
        list = await bridge.request<ToolListItem[]>('tools.list');
      }
      if (disposed) return;
      setTools(list);
      await refreshSessions();
      await refreshWorkdirs();
      const preferred = list.find((t) => t.tool.id === info.lastUsed?.toolId) ?? list[0];
      if (preferred) await selectTool(preferred.tool.id);
    })().catch((e) => console.error('启动加载失败', e));
    const off = bridge.on('sessions.changed', () => { refreshSessions(); });
    return () => { disposed = true; off(); };
  }, [refreshSessions, refreshWorkdirs, selectTool]);

  const handleRescan = useCallback(async () => {
    setScanning(true);
    try {
      setTools(await bridge.request<ToolListItem[]>('tools.rescan'));
      setAppInfo(await bridge.request<AppInfo>('app.info'));
    } catch (e) {
      console.error('重新扫描失败', e); // Toast 在任务 16 接入
    } finally { setScanning(false); }
  }, []);

  const handleAddTool = useCallback(async (name: string, exePath: string) => {
    const list = await bridge.request<ToolListItem[]>('tools.addManual', { name, exePath });
    setTools(list);
    setAddOpen(false);
  }, []);

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

  const termHidden = view === 'tools' || view === 'settings';
  const selectedTool = tools.find((t) => t.tool.id === selectedToolId) ?? null;

  return (
    <div className={`app${termHidden ? ' term-hidden' : ''}`}>
      <Rail view={view} onView={setView} version={appInfo ? `v${appInfo.version} · Windows` : ''} />
      <TopBar title={VIEW_TITLES[view]} userName={settingsInfo?.userName ?? ''} onRefresh={handleRescan} />
      <main className="main" id="content">
        <section className="view-panel" data-view-panel="launcher" hidden={view !== 'launcher'}>
          <LauncherView
            tools={tools} scanning={scanning} selectedToolId={selectedToolId}
            sessions={sessions} appInfo={appInfo}
            onSelectTool={selectTool} onRescan={handleRescan}
            onAddTool={() => setAddOpen(true)}
            configPanel={
              <section className="panel">
                <div className="panel-head"><span className="panel-title">启动配置</span></div>
                <div className="config">
                  {selectedTool
                    ? <p className="sub">「{selectedTool.tool.name}」的配置面板在任务 14 接入。</p>
                    : <p className="sub">从左侧选择一个工具。</p>}
                </div>
              </section>
            } />
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
      <AddToolModal open={addOpen} onClose={() => setAddOpen(false)} onConfirm={handleAddTool} />
    </div>
  );
}
