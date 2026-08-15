import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import { Rail, type View } from './Rail';
import { TopBar } from './TopBar';
import { LauncherView } from './LauncherView';
import { TerminalPanel } from './TerminalPanel';
import { AddToolModal } from './AddToolModal';
import { ConfigPanel } from './ConfigPanel';
import { FolderPickerModal } from './FolderPickerModal';
import { ToolsView } from './ToolsView';
import { SessionsView } from './SessionsView';
import { SettingsView } from './SettingsView';
import { Toast, type ToastItem } from './Toast';
import type { AppInfo, AppSettings, LaunchProfile, SettingsInfo, TerminalSessionInfo, ToolListItem } from './types';

const VIEW_TITLES: Record<View, string> = { launcher: '快速启动', tools: '工具库', sessions: '终端会话', settings: '设置' };

export default function App() {
  const [view, setView] = useState<View>('launcher');
  const [tools, setTools] = useState<ToolListItem[]>([]);
  const [sessions, setSessions] = useState<TerminalSessionInfo[]>([]);
  const [settingsInfo, setSettingsInfo] = useState<SettingsInfo | null>(null);
  const [appInfo, setAppInfo] = useState<AppInfo | null>(null);
  const [workdirs, setWorkdirs] = useState<string[]>([]);
  const [selectedToolId, setSelectedToolId] = useState<string | null>(null);
  const [profile, setProfile] = useState<LaunchProfile | null>(null);
  const [scanning, setScanning] = useState(false);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const toast = useCallback((text: string, kind: ToastItem['kind'] = 'info') => {
    const item: ToastItem = { id: Date.now() + Math.random(), text, kind };
    setToasts((prev) => [...prev, item]);
    setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== item.id)), 3200);
  }, []);

  const refreshSessions = useCallback(async () => {
    const list = await bridge.request<TerminalSessionInfo[]>('sessions.list');
    setSessions(list);
    // 数据到达期一并校正激活：cur 仍有效则保留（新建标签的显式 set 不被旧列表回退），失效则选剩余首个，全关归 null
    setActiveSessionId((cur) => (cur && list.some((s) => s.sessionId === cur) ? cur : list[0]?.sessionId ?? null));
  }, []);
  const refreshWorkdirs = useCallback(async () => {
    setWorkdirs(await bridge.request<string[]>('workdirs.list'));
  }, []);

  // 竞态防护：快速连点工具时 profiles.get 响应可能乱序到达，回调式校验丢弃非最新请求的过期 profile
  const latestToolIdRef = useRef<string | null>(null);

  const selectTool = useCallback(async (toolId: string) => {
    latestToolIdRef.current = toolId;
    setSelectedToolId(toolId);
    const p = await bridge.request<LaunchProfile>('profiles.get', { toolId });
    setProfile((cur) => (latestToolIdRef.current === p.toolId ? p : cur));
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
    })().catch((e: any) => { console.error('启动加载失败', e); toast(e.message, 'error'); });
    const off = bridge.on('sessions.changed', () => { refreshSessions(); });
    return () => { disposed = true; off(); };
  }, [refreshSessions, refreshWorkdirs, selectTool, toast]);

  const handleRescan = useCallback(async () => {
    setScanning(true);
    try {
      setTools(await bridge.request<ToolListItem[]>('tools.rescan'));
      setAppInfo(await bridge.request<AppInfo>('app.info'));
    } catch (e: any) {
      console.error('重新扫描失败', e);
      toast(e.message, 'error');
    } finally { setScanning(false); }
  }, [toast]);

  const handleAddTool = useCallback(async (name: string, exePath: string) => {
    try {
      const list = await bridge.request<ToolListItem[]>('tools.addManual', { name, exePath });
      setTools(list);
      setAddOpen(false);
      toast('已添加到工具库');
    } catch (e: any) {
      toast(e.message, 'error');
      throw e; // AddToolModal 内继续就地展示同一错误并复位提交态
    }
  }, [toast]);

  const handleSaveProfile = useCallback(async (p: LaunchProfile) => {
    try {
      const saved = await bridge.request<LaunchProfile>('profiles.save', { profile: p });
      // 同 selectTool 的竞态防护：保存响应晚于工具切换到达时（cur 已是其他工具）丢弃，避免覆盖新选中项
      setProfile((cur) => (cur && cur.id === saved.id ? saved : cur));
      toast('已保存');
    } catch (e: any) {
      console.error('保存配置失败', e);
      toast(e.message, 'error');
    }
  }, [toast]);

  const handleLaunch = useCallback(async (p: LaunchProfile) => {
    const tool = tools.find((t) => t.tool.id === p.toolId);
    if (!tool) return;
    try {
      await bridge.request('profiles.save', { profile: p }); // 启动即保存当前配置
      if (p.openMode === 'embedded') {
        const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.create',
          { toolId: p.toolId, profileId: p.id, cols: 120, rows: 30 });
        setActiveSessionId(sessionId); // 显式激活新标签；refreshSessions 的函数式校正不会覆盖仍在列表中的 cur
        await refreshSessions();
      } else {
        await bridge.request('launch.external', { toolId: p.toolId, profileId: p.id });
        toast(`已在独立窗口打开 ${tool.tool.name}`);
      }
      setAppInfo(await bridge.request<AppInfo>('app.info'));
      await refreshWorkdirs();
    } catch (e: any) {
      console.error('启动失败', e);
      toast(e.message, 'error');
    }
  }, [tools, refreshSessions, refreshWorkdirs, toast]);

  const handleNewShell = useCallback(async () => {
    try {
      const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.createShell', { cols: 120, rows: 30 });
      setActiveSessionId(sessionId);
      await refreshSessions();
    } catch (e: any) {
      console.error('新建会话失败', e);
      toast(e.message, 'error');
    }
  }, [refreshSessions, toast]);

  const handleCloseSession = useCallback(async (id: string) => {
    await bridge.request('terminal.close', { sessionId: id }).catch(() => {});
    await refreshSessions();
  }, [refreshSessions]);

  const handleSaveSettings = useCallback(async (settings: AppSettings) => {
    try {
      setSettingsInfo(await bridge.request<SettingsInfo>('settings.save', { settings }));
      toast('设置已保存');
    } catch (e: any) {
      console.error('保存设置失败', e);
      toast(e.message, 'error');
    }
  }, [toast]);

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
            configPanel={selectedTool && profile ? (
              <ConfigPanel
                tool={selectedTool} profile={profile} workdirs={workdirs}
                onBrowse={() => setPickerOpen(true)}
                onSave={handleSaveProfile}
                onLaunch={handleLaunch} />
            ) : (
              <section className="panel">
                <div className="panel-head"><span className="panel-title">启动配置</span></div>
                <div className="config"><p className="sub">从左侧选择一个工具。</p></div>
              </section>
            )} />
        </section>
        <section className="view-panel" data-view-panel="tools" hidden={view !== 'tools'}>
          <ToolsView tools={tools} onRescan={() => { setView('launcher'); handleRescan(); }} />
        </section>
        <section className="view-panel" data-view-panel="sessions" hidden={view !== 'sessions'}>
          <SessionsView sessions={sessions} onNewShell={handleNewShell} />
        </section>
        <section className="view-panel" data-view-panel="settings" hidden={view !== 'settings'}>
          {settingsInfo && <SettingsView info={settingsInfo} onSave={handleSaveSettings} />}
        </section>
      </main>
      <TerminalPanel sessions={sessions} activeId={activeSessionId}
        onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
      <AddToolModal open={addOpen} onClose={() => setAddOpen(false)} onConfirm={handleAddTool} />
      <FolderPickerModal
        open={pickerOpen}
        initialValue={profile?.workdir || ''}
        commonDirs={settingsInfo?.commonDirs ?? []}
        workdirs={workdirs}
        onConfirm={(path) => { setProfile((p) => (p ? { ...p, workdir: path } : p)); setPickerOpen(false); }}
        onClose={() => setPickerOpen(false)} />
      <Toast items={toasts} />
    </div>
  );
}
