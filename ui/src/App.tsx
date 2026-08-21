import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import { Rail, type View } from './Rail';
import { TopBar } from './TopBar';
import { LauncherView } from './LauncherView';
import { TerminalPanel } from './TerminalPanel';
import { AddToolModal } from './AddToolModal';
import { ClosePromptModal } from './ClosePromptModal';
import { ConfigPanel } from './ConfigPanel';
import { ToolsView } from './ToolsView';
import { SettingsView } from './SettingsView';
import { Toast, type ToastItem } from './Toast';
import type { AppInfo, AppSettings, HiddenTool, LaunchProfile, SettingsInfo, TerminalSessionInfo, ToolListItem } from './types';

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
  const [profiles, setProfiles] = useState<LaunchProfile[]>([]);
  const [hidden, setHidden] = useState<HiddenTool[]>([]);
  const [renameError, setRenameError] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [closePromptOpen, setClosePromptOpen] = useState(false);
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
  const refreshHidden = useCallback(async () => {
    setHidden(await bridge.request<HiddenTool[]>('tools.hidden'));
  }, []);

  // 竞态防护：快速连点工具时 profiles.get 响应可能乱序到达，回调式校验丢弃非最新请求的过期 profile
  const latestToolIdRef = useRef<string | null>(null);

  const selectTool = useCallback(async (toolId: string) => {
    latestToolIdRef.current = toolId;
    setSelectedToolId(toolId);
    setRenameError(null);
    const [p, list] = await Promise.all([
      bridge.request<LaunchProfile>('profiles.get', { toolId }),
      bridge.request<LaunchProfile[]>('profiles.list', { toolId }),
    ]);
    if (latestToolIdRef.current !== p.toolId) return;
    setProfile(p);
    setProfiles(list);
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
      await refreshHidden();
      const preferred = list.find((t) => t.tool.id === info.lastUsed?.toolId) ?? list[0];
      if (preferred) await selectTool(preferred.tool.id);
    })().catch((e: any) => { console.error('启动加载失败', e); toast(e.message, 'error'); });
    const off = bridge.on('sessions.changed', () => { refreshSessions(); });
    return () => { disposed = true; off(); };
  }, [refreshSessions, refreshWorkdirs, refreshHidden, selectTool, toast]);

  const applyTools = useCallback(async (list: ToolListItem[]) => {
    setTools(list);
    await refreshHidden();
    setSelectedToolId((cur) => {
      if (cur && list.some((t) => t.tool.id === cur)) return cur;
      const next = list[0]?.tool.id ?? null;
      if (next) void selectTool(next);
      else { setProfile(null); setProfiles([]); }
      return next;
    });
  }, [refreshHidden, selectTool]);

  const handleRescan = useCallback(async () => {
    setScanning(true);
    try {
      await applyTools(await bridge.request<ToolListItem[]>('tools.rescan'));
      setAppInfo(await bridge.request<AppInfo>('app.info'));
    } catch (e: any) {
      console.error('重新扫描失败', e);
      toast(e.message, 'error');
    } finally { setScanning(false); }
  }, [toast, applyTools]);

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

  // 系统目录选择对话框（WPF OpenFolderDialog 经桥调用；浏览器 Mock 返回模拟路径）
  const handleBrowseWorkdir = useCallback(async () => {
    try {
      const r = await bridge.request<{ path: string } | null>('dialog.selectDirectory', {
        initial: profile?.workdir || '',
      });
      if (r?.path) setProfile((cur) => (cur ? { ...cur, workdir: r.path } : cur));
    } catch (e) {
      console.error('选择目录失败', e);
    }
  }, [profile?.workdir]);

  const persistProfile = useCallback(async (p: LaunchProfile) => {
    const saved = await bridge.request<LaunchProfile>('profiles.save', { profile: p });
    setProfile((cur) => (cur && cur.id === saved.id ? saved : cur));
    return saved;
  }, []);

  const handleSaveProfile = useCallback(async (p: LaunchProfile) => {
    try {
      await persistProfile(p);
      toast('已保存');
    } catch (e: any) {
      console.error('保存配置失败', e);
      toast(e.message, 'error');
      throw e; // rethrow 让 ConfigPanel 的 catch 兜住——失败时不闪"已保存"
    }
  }, [persistProfile, toast]);

  const handleSwitchProfile = useCallback(async (draft: LaunchProfile, nextId: string) => {
    if (draft.id === nextId) return;
    try {
      await persistProfile(draft);
      const next = await bridge.request<LaunchProfile>('profiles.select', { toolId: draft.toolId, profileId: nextId });
      setProfile(next);
      setProfiles(await bridge.request<LaunchProfile[]>('profiles.list', { toolId: draft.toolId }));
      setRenameError(null);
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [persistProfile, toast]);

  const handleCreateProfile = useCallback(async (draft: LaunchProfile) => {
    try {
      await persistProfile(draft);
      const created = await bridge.request<LaunchProfile>('profiles.create', { toolId: draft.toolId, fromProfileId: draft.id });
      setProfile(created);
      setProfiles(await bridge.request<LaunchProfile[]>('profiles.list', { toolId: draft.toolId }));
      setRenameError(null);
      toast('已新建配置');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [persistProfile, toast]);

  const handleRenameProfile = useCallback(async (id: string, name: string) => {
    try {
      const renamed = await bridge.request<LaunchProfile>('profiles.rename', { id, name });
      setProfiles(await bridge.request<LaunchProfile[]>('profiles.list', { toolId: renamed.toolId }));
      setProfile((cur) => (cur && cur.id === renamed.id ? { ...cur, name: renamed.name } : cur));
      setRenameError(null);
      return true;
    } catch (e: any) {
      setRenameError(e.message);
      return false;
    }
  }, []);

  const handleDeleteProfile = useCallback(async (id: string) => {
    try {
      const next = await bridge.request<LaunchProfile>('profiles.delete', { id });
      setProfile(next);
      setProfiles(await bridge.request<LaunchProfile[]>('profiles.list', { toolId: next.toolId }));
      setRenameError(null);
      toast('已删除配置');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [toast]);

  const handleHideTool = useCallback(async (toolId: string) => {
    try {
      await applyTools(await bridge.request<ToolListItem[]>('tools.hide', { toolId }));
      toast('已隐藏');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [applyTools, toast]);

  const handleUnhideTool = useCallback(async (exePath: string) => {
    try {
      await applyTools(await bridge.request<ToolListItem[]>('tools.unhide', { exePath }));
      toast('已取消隐藏');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [applyTools, toast]);

  const handleDeleteTool = useCallback(async (toolId: string) => {
    if (!confirm('删除该工具及其全部启动配置？此操作不可撤销。')) return;
    try {
      await applyTools(await bridge.request<ToolListItem[]>('tools.delete', { toolId }));
      toast('已删除');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [applyTools, toast]);

  const handleRelocateTool = useCallback(async (toolId: string) => {
    try {
      const current = tools.find((t) => t.tool.id === toolId);
      const r = await bridge.request<{ path: string } | null>('dialog.selectFile', {
        initial: current?.tool.exePath || '',
      });
      if (!r?.path) return;
      await bridge.request('tools.relocate', { toolId, exePath: r.path });
      setTools(await bridge.request<ToolListItem[]>('tools.list'));
      toast('已更新可执行路径');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [tools, toast]);

  const handleLaunch = useCallback(async (p: LaunchProfile) => {
    const tool = tools.find((t) => t.tool.id === p.toolId);
    if (!tool) return;
    try {
      await bridge.request('profiles.save', { profile: p }); // 启动即保存当前配置
      if (p.openMode === 'embedded') {
        const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.create',
          { toolId: p.toolId, profileId: p.id, cols: 120, rows: 30 });
        setActiveSessionId(sessionId); // 显式激活新标签；refreshSessions 的函数式校正不会覆盖仍在列表中的 cur
        setView('sessions'); // 内嵌启动后进入终端主舞台
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
      setView('sessions');
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

  const applyCloseChoice = useCallback(async (action: 'tray' | 'exit', remember: boolean) => {
    setClosePromptOpen(false);
    if (remember && settingsInfo) {
      const closeBehavior = action === 'tray' ? 'minimizeToTray' : 'exit' as const;
      try {
        setSettingsInfo(await bridge.request<SettingsInfo>('settings.save', {
          settings: { ...settingsInfo.settings, closeBehavior },
        }));
      } catch (e: any) {
        toast(e.message, 'error');
      }
    }
    try {
      await bridge.request(action === 'tray' ? 'window.hideToTray' : 'window.exit');
    } catch (e: any) {
      toast(e.message, 'error');
    }
  }, [settingsInfo, toast]);

  useEffect(() => {
    const offPrompt = bridge.on('window.close.prompt', () => setClosePromptOpen(true));
    const offTray = bridge.on('window.tray.mocked', () => toast('已最小化到托盘（仅桌面壳生效）'));
    return () => { offPrompt(); offTray(); };
  }, [toast]);

  const termStage = view === 'sessions';
  const selectedTool = tools.find((t) => t.tool.id === selectedToolId) ?? null;

  return (
    <div className={`app${termStage ? ' term-stage' : ''}`}>
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
                tool={selectedTool} profile={profile} profiles={profiles} workdirs={workdirs}
                onBrowse={handleBrowseWorkdir}
                onSave={handleSaveProfile}
                onLaunch={handleLaunch}
                onSwitchProfile={handleSwitchProfile}
                onCreateProfile={handleCreateProfile}
                onRenameProfile={handleRenameProfile}
                onDeleteProfile={handleDeleteProfile}
                renameError={renameError} />
            ) : (
              <section className="panel">
                <div className="panel-head"><span className="panel-title">启动配置</span></div>
                <div className="config"><p className="sub">从左侧选择一个工具。</p></div>
              </section>
            )} />
        </section>
        <section className="view-panel" data-view-panel="tools" hidden={view !== 'tools'}>
          <ToolsView tools={tools} hidden={hidden}
            onRescan={() => { setView('launcher'); handleRescan(); }}
            onHide={handleHideTool} onDelete={handleDeleteTool}
            onRelocate={handleRelocateTool} onUnhide={handleUnhideTool} />
        </section>
        <section className="view-panel" data-view-panel="sessions" hidden={view !== 'sessions'}>
          <TerminalPanel visible={termStage} sessions={sessions} activeId={activeSessionId}
            onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
        </section>
        <section className="view-panel" data-view-panel="settings" hidden={view !== 'settings'}>
          {settingsInfo && <SettingsView info={settingsInfo} onSave={handleSaveSettings} />}
        </section>
      </main>
      <AddToolModal open={addOpen} onClose={() => setAddOpen(false)} onConfirm={handleAddTool} />
      <ClosePromptModal
        open={closePromptOpen}
        onDismiss={() => setClosePromptOpen(false)}
        onMinimize={(remember) => void applyCloseChoice('tray', remember)}
        onExit={(remember) => void applyCloseChoice('exit', remember)} />
      <Toast items={toasts} />
    </div>
  );
}
