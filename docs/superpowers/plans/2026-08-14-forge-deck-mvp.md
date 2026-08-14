# ForgeDeck MVP 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 按设计稿实现本地 AI 编程工具快速启动器——扫描已装工具、维护启动配置（参数/环境变量/工作目录/打开方式）、内嵌 ConPTY 终端与独立窗口两种启动方式。

**架构：** WPF 壳承载 WebView2，前端 React+TS+xterm.js 渲染全部界面；C# 类库 ForgeDeck.Core 承载业务（扫描/配置/启动/终端会话），经 JSON 消息桥（WebView2 WebMessage）双向通信；发布时前端 dist 复制进 App 资源。

**技术栈：** .NET 8 WPF、Microsoft.Web.WebView2 1.0.4129.50、Porta.Pty 1.0.7（ConPTY）、xUnit、Vite + React 19 + TypeScript、@xterm/xterm + @xterm/addon-fit。

**规格：** `docs/superpowers/specs/2026-08-14-launcher-design.md`（本计划的唯一需求来源）
**设计契约：** `docs/design/Web-Prototype/ai-tool-launcher.html`（像素级视觉与交互契约，含全部令牌/类名/DOM 结构）

**环境约定（所有任务通用）：**
- 仓库根：`C:\workspace\ForgeDeck`（Git Bash 路径 `/c/workspace/ForgeDeck`），直接在 main 分支工作（新仓库，无并行开发）。
- 后端命令在仓库根执行：`dotnet test`、`dotnet build`；前端命令在 `ui/` 下执行：`npm run build`。
- 联调：终端 A `cd ui && npm run dev`；终端 B 仓库根 `FORGEDECK_DEV=1 dotnet run --project src/ForgeDeck.App`。
- Windows 测试环境本机即满足（ConPTY 需 Win10 1809+）。终端集成测试为 Windows-only，本机可直接跑。
- C# 序列化统一走 `JsonOptions`（camelCase + 枚举 camelCase 字符串），前端类型与之一致。

---

## 文件结构总览

```
src/ForgeDeck.Core/
  Models.cs                      ToolType/OpenMode/ToolInfo/LaunchProfile/AppSettings/AppConfig/LastUsedInfo
  JsonOptions.cs                 统一 JsonSerializerOptions
  Config/ConfigStore.cs          配置读写（原子写/损坏恢复）
  Config/WorkdirHistoryService.cs 工作目录历史
  Scanning/KnownTools.cs         内置已知工具目录
  Scanning/PathSearch.cs         PATH/目录探测工具
  Scanning/IScanSource.cs        扫描源接口与 ScanHit/ScanContext
  Scanning/KnownDirsScanSource.cs 已知安装目录 + 附加目录扫描
  Scanning/PathScanSource.cs     PATH 扫描
  Scanning/RegistryScanSource.cs 注册表卸载项扫描（含 IUninstallRegistry）
  Scanning/StartMenuScanSource.cs 开始菜单 .lnk 扫描（含 IShellLinkResolver）
  Scanning/ToolScanner.cs        聚合去重
  Launching/LaunchService.cs     校验/命令包装/env 展开/外部启动
  Terminal/TerminalSessionManager.cs ConPTY 会话管理
  Bridge/BridgeException.cs      业务错误（带 code）
  Bridge/BridgeDispatcher.cs     JSON 分发器（请求/响应/事件）
  Bridge/ForgeDeckBridge.cs      方法注册与业务接线
src/ForgeDeck.App/
  MainWindow.xaml / .cs          WebView2 宿主 + 消息桥接线 + 退出确认
tests/ForgeDeck.Core.Tests/      每个 Core 模块对应测试类
ui/src/
  app.css                        设计稿样式整体迁移 + 少量补充
  types.ts / bridge.ts           TS 类型 + 桥接客户端（含浏览器 Mock）
  lib/env.ts, lib/format.ts      env 文本解析 / 相对时间等
  App.tsx                        状态中枢 + 布局
  Rail.tsx, TopBar.tsx           侧边栏 / 顶栏
  LauncherView.tsx               快速启动页（指标 + 双栏）
  ToolListPanel.tsx, ConfigPanel.tsx, WorkdirControl.tsx
  Modal.tsx, AddToolModal.tsx, FolderPickerModal.tsx, Switch.tsx
  ToolsView.tsx, SessionsView.tsx, SettingsView.tsx
  TerminalPanel.tsx, Toast.tsx
```

---

## 任务 1：设计样式整体迁移

**文件：**
- 创建：`ui/src/app.css`
- 修改：`ui/src/main.tsx`、`ui/index.html`
- 删除：`ui/src/index.css`、`ui/src/App.css`、`ui/src/assets/`

- [ ] **步骤 1：迁移设计稿样式**

把 `docs/design/Web-Prototype/ai-tool-launcher.html` 第 7-35 行 `<style>` 内的全部 CSS **原样**复制到 `ui/src/app.css`，并在文件末尾追加以下补充样式（Toast、xterm 容器、退出态圆点）：

```css
/* —— 产品补充（设计稿未覆盖的部分，令牌与设计一致） —— */
.toast-wrap{position:fixed;right:18px;bottom:calc(var(--terminal) + 18px);display:grid;gap:8px;z-index:60}
.toast{background:var(--surface);border:1px solid var(--border);border-left:2px solid var(--accent);border-radius:6px;padding:11px 14px;font-size:12px;color:var(--fg);box-shadow:0 14px 38px color-mix(in oklch,var(--bg) 72%,transparent);animation:view-item-in .2s cubic-bezier(.2,0,0,1) both;max-width:420px}
.toast.error{border-left-color:#e5484d}
.term-body{padding:10px 16px}
.term-body .xterm{height:100%}
.term-tab .status-dot.exited{background:var(--muted);box-shadow:none}
```

- [ ] **步骤 2：清理 Vite 模板并接线**

`ui/src/main.tsx` 改为：

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './app.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

删除 `ui/src/index.css`、`ui/src/App.css`、`ui/src/assets/` 目录。`ui/index.html` 的 `<title>` 改为 `ForgeDeck</title>`（去掉 Vite 后缀）。

- [ ] **步骤 3：验证构建**

运行：`cd ui && npm run build`
预期：`✓ built`，无 TS/CSS 报错。

- [ ] **步骤 4：Commit**

```bash
git add ui/src ui/index.html
git commit -m "feat(ui): 迁移设计稿令牌与全局样式"
```

---

## 任务 2：TS 类型与桥接客户端（含浏览器 Mock）

**文件：**
- 创建：`ui/src/types.ts`、`ui/src/bridge.ts`

- [ ] **步骤 1：编写类型定义**

`ui/src/types.ts`（与 C# 序列化输出一一对应，全部 camelCase）：

```ts
export type ToolType = 'cli' | 'gui';
export type OpenMode = 'embedded' | 'external';

export interface ToolInfo {
  id: string;
  name: string;
  type: ToolType;
  exePath: string;
  source: string;
  builtin: boolean;
  manual: boolean;
}

export interface ToolListItem {
  tool: ToolInfo;
  exists: boolean;
  defaultMode: OpenMode;
}

export interface LaunchProfile {
  id: string;
  toolId: string;
  name: string;
  args: string;
  env: Record<string, string>;
  workdir: string;
  openMode: OpenMode;
  autoRestore: boolean;
}

export interface AppSettings {
  defaultShell: 'pwsh' | 'powershell' | 'cmd';
  autoScanOnStartup: boolean;
  extraScanDirs: string[];
  skipExitConfirm: boolean;
  preferEmbedded: boolean;
  maxWorkdirHistory: number;
}

export interface CommonDir { name: string; path: string }

export interface SettingsInfo {
  settings: AppSettings;
  commonDirs: CommonDir[];
  userName: string;
}

export interface TerminalSessionInfo {
  sessionId: string;
  title: string;
  workdir: string;
  running: boolean;
  exitCode: number | null;
}

export interface AppInfo {
  version: string;
  userName: string;
  lastScanAt: string | null;
  lastUsed: { toolId: string; workdir: string } | null;
}
```

- [ ] **步骤 2：编写桥接客户端**

`ui/src/bridge.ts`：

```ts
import type { AppInfo, LaunchProfile, SettingsInfo, TerminalSessionInfo, ToolListItem } from './types';

export interface Bridge {
  request<T = unknown>(method: string, params?: unknown): Promise<T>;
  on(event: string, listener: (data: any) => void): () => void;
}

interface WebView {
  postMessage(message: any): void;
  addEventListener(type: 'message', listener: (e: { data: any }) => void): void;
}

declare global {
  interface Window { chrome?: { webview?: WebView } }
}

class WebViewBridge implements Bridge {
  private seq = 0;
  private readonly pending = new Map<number, { resolve: (v: any) => void; reject: (e: Error) => void }>();
  private readonly listeners = new Map<string, Set<(data: any) => void>>();

  constructor() {
    window.chrome!.webview!.addEventListener('message', (e) => this.receive(e.data));
  }

  request<T = unknown>(method: string, params?: unknown): Promise<T> {
    const id = ++this.seq;
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      window.chrome!.webview!.postMessage(JSON.stringify({ id, method, params }));
    });
  }

  on(event: string, listener: (data: any) => void): () => void {
    const set = this.listeners.get(event) ?? new Set();
    set.add(listener);
    this.listeners.set(event, set);
    return () => set.delete(listener);
  }

  private receive(msg: any) {
    if (msg && typeof msg === 'object' && 'id' in msg) {
      const entry = this.pending.get(msg.id);
      if (!entry) return;
      this.pending.delete(msg.id);
      if ('error' in msg && msg.error) entry.reject(new Error(`${msg.error.code}: ${msg.error.message}`));
      else entry.resolve(msg.result ?? null);
      return;
    }
    if (msg && typeof msg === 'object' && 'event' in msg) {
      this.listeners.get(msg.event)?.forEach((fn) => fn(msg.data));
    }
  }
}

/** 纯浏览器开发（无 WebView2 宿主）时的 Mock：数据与设计稿一致，便于纯前端调 UI。 */
class MockBridge implements Bridge {
  private seq = 0;
  private readonly listeners = new Map<string, Set<(data: any) => void>>();
  private sessions: TerminalSessionInfo[] = [];
  private readonly workdirs = ['C:\\Projects\\atlas-web', 'C:\\Projects\\forge-launcher', 'D:\\Workspaces\\ai-labs'];
  private readonly tools: ToolListItem[] = [
    { tool: { id: 't-claude', name: 'Claude Code', type: 'cli', exePath: 'C:\\Users\\dev\\AppData\\Roaming\\npm\\claude.cmd', source: 'npm 全局', builtin: true, manual: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-codex', name: 'Codex CLI', type: 'cli', exePath: 'C:\\Users\\dev\\.local\\bin\\codex.exe', source: '用户目录', builtin: true, manual: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-cursor', name: 'Cursor Agent', type: 'cli', exePath: 'C:\\Program Files\\Cursor\\resources\\app\\bin\\cursor-agent.exe', source: '开始菜单', builtin: true, manual: false }, exists: true, defaultMode: 'external' },
    { tool: { id: 't-aider', name: 'Aider', type: 'cli', exePath: 'C:\\Users\\dev\\AppData\\Local\\Programs\\Python\\Scripts\\aider.exe', source: 'Python Scripts', builtin: true, manual: false }, exists: true, defaultMode: 'embedded' },
  ];
  private readonly settings: SettingsInfo = {
    settings: { defaultShell: 'pwsh', autoScanOnStartup: true, extraScanDirs: [], skipExitConfirm: false, preferEmbedded: true, maxWorkdirHistory: 20 },
    commonDirs: [
      { name: '主目录', path: 'C:\\Users\\dev' },
      { name: '桌面', path: 'C:\\Users\\dev\\Desktop' },
      { name: '文档', path: 'C:\\Users\\dev\\Documents' },
      { name: 'C:\\', path: 'C:\\' },
    ],
    userName: 'Dev',
  };
  private readonly profiles = new Map<string, LaunchProfile>();

  request<T = unknown>(method: string, params?: any): Promise<T> {
    return new Promise((resolve, reject) => setTimeout(() => {
      try { resolve(this.handle(method, params) as T); }
      catch (e: any) { reject(new Error(`mock: ${e.message}`)); }
    }, 80));
  }

  on(event: string, listener: (data: any) => void): () => void {
    const set = this.listeners.get(event) ?? new Set();
    set.add(listener);
    this.listeners.set(event, set);
    return () => set.delete(listener);
  }

  private emit(event: string, data: any) {
    this.listeners.get(event)?.forEach((fn) => fn(data));
  }

  private handle(method: string, p: any): any {
    switch (method) {
      case 'app.info':
        return { version: '0.1.0', userName: this.settings.userName, lastScanAt: new Date().toISOString(), lastUsed: { toolId: 't-claude', workdir: 'C:\\Projects\\atlas-web' } } satisfies AppInfo;
      case 'tools.list':
      case 'tools.rescan':
        return this.tools;
      case 'tools.addManual': {
        if (!p.name.trim()) throw new Error('工具名称不能为空');
        const id = `t-manual-${++this.seq}`;
        this.tools.push({ tool: { id, name: p.name, type: 'cli', exePath: p.exePath, source: '手动添加', builtin: false, manual: true }, exists: true, defaultMode: 'embedded' });
        return this.tools;
      }
      case 'profiles.get': {
        const found = this.profiles.get(p.toolId);
        if (found) return found;
        const fresh: LaunchProfile = { id: `p-${++this.seq}`, toolId: p.toolId, name: '默认', args: '', env: {}, workdir: '', openMode: 'embedded', autoRestore: false };
        this.profiles.set(p.toolId, fresh);
        return fresh;
      }
      case 'profiles.save':
        this.profiles.set(p.profile.toolId, p.profile);
        return p.profile;
      case 'settings.get':
        return this.settings;
      case 'settings.save':
        Object.assign(this.settings.settings, p.settings);
        return this.settings;
      case 'workdirs.list':
        return this.workdirs;
      case 'workdirs.add':
        this.workdirs.unshift(p.path);
        this.workdirs.splice(5);
        return this.workdirs;
      case 'workdirs.remove':
        this.workdirs.splice(this.workdirs.indexOf(p.path), 1);
        return this.workdirs;
      case 'sessions.list':
        return this.sessions;
      case 'terminal.create':
      case 'terminal.createShell': {
        const title = method === 'terminal.createShell' ? 'pwsh' : this.tools.find((t) => t.tool.id === p.toolId)?.tool.name ?? '会话';
        const id = `s-${++this.seq}`;
        this.sessions.push({ sessionId: id, title, workdir: 'C:\\Projects\\atlas-web', running: true, exitCode: null });
        this.emit('sessions.changed', {});
        this.mockOutput(id, title);
        return { sessionId: id };
      }
      case 'terminal.write':
      case 'terminal.resize':
        return null;
      case 'terminal.kill':
      case 'terminal.close': {
        const session = this.sessions.find((s) => s.sessionId === p.sessionId);
        if (session) { session.running = false; }
        if (method === 'terminal.close') this.sessions = this.sessions.filter((s) => s.sessionId !== p.sessionId);
        this.emit('sessions.changed', {});
        return null;
      }
      case 'launch.external':
        return { pid: 4242 };
      default:
        throw new Error(`未知方法：${method}`);
    }
  }

  private mockOutput(id: string, title: string) {
    const lines = [`${title} · Mock 终端（浏览器预览）\r\n`, '在 WebView2 宿主中运行时连接真实 ConPTY。\r\n'];
    lines.forEach((chunk, i) => setTimeout(() => this.emit('terminal.data', { sessionId: id, chunk }), 300 * (i + 1)));
  }
}

export const bridge: Bridge = window.chrome?.webview ? new WebViewBridge() : new MockBridge();
```

- [ ] **步骤 3：验证构建**

运行：`cd ui && npm run build`
预期：`✓ built`。（此时 `App.tsx` 仍是模板内容，未引用 bridge 也可编译；若模板引用了已删文件导致报错，将 `App.tsx` 临时改为 `export default function App() { return <div className="app" /> }`，任务 3 会重写。）

- [ ] **步骤 4：Commit**

```bash
git add ui/src/types.ts ui/src/bridge.ts ui/src/App.tsx
git commit -m "feat(ui): 桥接客户端与类型定义（WebView2 + 浏览器 Mock）"
```

---

## 任务 3：应用壳（侧栏/顶栏/视图切换/终端占位）

**文件：**
- 创建：`ui/src/Rail.tsx`、`ui/src/TopBar.tsx`
- 重写：`ui/src/App.tsx`

- [ ] **步骤 1：Rail（左侧导航）**

`ui/src/Rail.tsx`——DOM 结构与类名对齐设计稿（第 39-49 行），图标 SVG 原样复制：

```tsx
export type View = 'launcher' | 'tools' | 'sessions' | 'settings';

const ICONS = {
  launcher: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="4" width="6" height="6" /><rect x="14" y="4" width="6" height="6" /><rect x="4" y="14" width="6" height="6" /><rect x="14" y="14" width="6" height="6" /></svg>,
  tools: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M14.7 6.3a4.2 4.2 0 0 0-5.9 5.9L4 17v3h3l4.8-4.8a4.2 4.2 0 0 0 5.9-5.9l-2.1 2.1-2.8-2.8 1.9-2.3Z" /></svg>,
  sessions: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="m7 9 3 3-3 3m5 0h5" /></svg>,
  settings: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L7 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H5.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L7 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" /></svg>,
};

export function Rail({ view, onView, version }: { view: View; onView: (v: View) => void; version: string }) {
  const item = (v: View, label: string) => (
    <button className={view === v ? 'active' : ''} aria-current={view === v ? 'page' : undefined} onClick={() => onView(v)}>
      {ICONS[v]}<span>{label}</span>
    </button>
  );
  return (
    <aside className="rail">
      <div className="brand">
        <div className="brand-mark">F/</div>
        <div><strong>forge</strong><small>TOOL LAUNCHER</small></div>
      </div>
      <div className="nav-label">工作台</div>
      <nav className="nav" aria-label="工作台">
        {item('launcher', '快速启动')}
        {item('tools', '工具库')}
        {item('sessions', '终端会话')}
      </nav>
      <div className="nav-label">系统</div>
      <nav className="nav" aria-label="系统">{item('settings', '设置')}</nav>
      <div className="rail-foot">
        <span className="status-dot" />扫描服务正常<br />
        <span className="mono">{version || 'v0.1.0 · Windows'}</span>
      </div>
    </aside>
  );
}
```

- [ ] **步骤 2：TopBar（顶栏）**

`ui/src/TopBar.tsx`：

```tsx
function initials(name: string): string {
  const parts = name.split(/[.\-_ ]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase() || 'F/';
}

export function TopBar({ title, userName, onRefresh }: { title: string; userName: string; onRefresh: () => void }) {
  return (
    <header className="top">
      <div className="crumb">工作台&nbsp; / &nbsp;<b>{title}</b></div>
      <div className="top-actions">
        <button className="icon-btn" title="刷新工具扫描" onClick={onRefresh}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M20 11a8.1 8.1 0 0 0-14.8-4L3 10m0-5v5h5M4 13a8.1 8.1 0 0 0 14.8 4L21 14m0 5v-5h-5" /></svg>
        </button>
        <button className="icon-btn" title="通知">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" /></svg>
        </button>
        <div className="avatar">{initials(userName)}</div>
      </div>
    </header>
  );
}
```

- [ ] **步骤 3：App 壳与视图切换**

`ui/src/App.tsx`（本任务先放四个占位视图，后续任务逐一替换为真组件）：

```tsx
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
```

- [ ] **步骤 4：验证构建与视觉**

运行：`cd ui && npm run build` → 预期 `✓ built`。
再运行 `npm run dev` 并浏览器打开 `http://localhost:5173`，对照设计稿检查：左侧栏、顶栏、四视图切换、工具库/设置视图下底部终端区隐藏（`term-hidden`）。

- [ ] **步骤 5：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 应用壳——侧栏/顶栏/视图切换/终端占位"
```

---

## 任务 4：Core 模型与 ConfigStore（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Models.cs`、`src/ForgeDeck.Core/JsonOptions.cs`、`src/ForgeDeck.Core/Config/ConfigStore.cs`
- 测试：`tests/ForgeDeck.Core.Tests/ConfigStoreTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/ConfigStoreTests.cs`：

```csharp
using ForgeDeck.Core;
using ForgeDeck.Core.Config;

namespace ForgeDeck.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => System.IO.Path.Combine(_dir, name);

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new ConfigStore(PathFor("config.json"));
        store.Load();
        Assert.Equal(1, store.Config.Version);
        Assert.True(store.Config.Settings.AutoScanOnStartup);
        Assert.Empty(store.Config.Tools);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProfile()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.Profiles.Add(new LaunchProfile
        {
            ToolId = "t1", Args = "--model x", Workdir = @"D:\work",
            Env = new() { ["A"] = "1" }, OpenMode = OpenMode.External, AutoRestore = true,
        });
        store.Save();

        var reloaded = new ConfigStore(path);
        reloaded.Load();
        var profile = Assert.Single(reloaded.Config.Profiles);
        Assert.Equal("t1", profile.ToolId);
        Assert.Equal(OpenMode.External, profile.OpenMode);
        Assert.True(profile.AutoRestore);
        Assert.Equal("1", profile.Env["A"]);
    }

    [Fact]
    public void Save_CreatesMissingDirectory_AndWritesNoTmpLeftover()
    {
        var path = PathFor("nested/deep/config.json");
        var store = new ConfigStore(path);
        store.Save();
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_BackupsAndReturnsDefaults()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, "{ not json !!!");
        var store = new ConfigStore(path);
        store.Load();
        Assert.Empty(store.Config.Tools);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_WritesCamelCaseContractForFrontend()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.Profiles.Add(new LaunchProfile { ToolId = "t1", OpenMode = OpenMode.External });
        store.Save();
        var json = File.ReadAllText(path);
        Assert.Contains("\"openMode\": \"external\"", json);
        Assert.Contains("\"toolId\": \"t1\"", json);
    }

    [Fact]
    public void Load_CorruptFile_BackupFails_StillReturnsDefaults()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, "{ not json !!!");
        Directory.CreateDirectory(path + ".bak"); // .bak 被目录占用 → File.Move 备份失败
        var store = new ConfigStore(path);
        store.Load();
        Assert.Empty(store.Config.Tools);
        Assert.True(File.Exists(path)); // 备份失败时保留原损坏文件，不崩溃
    }
}
```

运行：`dotnet test --filter ConfigStoreTests`
预期：编译失败（`LaunchProfile`/`ConfigStore` 不存在）。

- [ ] **步骤 2：实现模型与 ConfigStore**

`src/ForgeDeck.Core/Models.cs`：

```csharp
namespace ForgeDeck.Core;

public enum ToolType { Cli, Gui }
public enum OpenMode { Embedded, External }

public sealed class ToolInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ToolType Type { get; set; } = ToolType.Cli;
    public string ExePath { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Builtin { get; set; }
    public bool Manual { get; set; }
}

public sealed class LaunchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolId { get; set; } = "";
    public string Name { get; set; } = "默认";
    public string Args { get; set; } = "";
    public Dictionary<string, string> Env { get; set; } = new();
    public string Workdir { get; set; } = "";
    public OpenMode OpenMode { get; set; } = OpenMode.Embedded;
    public bool AutoRestore { get; set; }
}

public sealed class AppSettings
{
    public string DefaultShell { get; set; } = "pwsh";
    public bool AutoScanOnStartup { get; set; } = true;
    public List<string> ExtraScanDirs { get; set; } = new();
    public bool SkipExitConfirm { get; set; }
    public bool PreferEmbedded { get; set; } = true;
    public int MaxWorkdirHistory { get; set; } = 20;
}

public sealed class LastUsedInfo
{
    public string ToolId { get; set; } = "";
    public string Workdir { get; set; } = "";
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<ToolInfo> Tools { get; set; } = new();
    public List<LaunchProfile> Profiles { get; set; } = new();
    public Dictionary<string, List<string>> WorkdirHistory { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public DateTime? LastScanAt { get; set; }
    public LastUsedInfo? LastUsed { get; set; }
}
```

`src/ForgeDeck.Core/JsonOptions.cs`：

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeDeck.Core;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    public static JsonSerializerOptions Create(Action<JsonSerializerOptions>? configure = null)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        configure?.Invoke(opts);
        return opts;
    }
}
```

`src/ForgeDeck.Core/Config/ConfigStore.cs`：

```csharp
using System.Text.Json;
using ForgeDeck.Core;

namespace ForgeDeck.Core.Config;

public sealed class ConfigStore
{
    private readonly string _path;
    public AppConfig Config { get; private set; } = new();

    public ConfigStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ForgeDeck", "config.json");
    }

    public void Load()
    {
        if (!File.Exists(_path)) { Config = new AppConfig(); return; }
        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions.Default)
                     ?? new AppConfig();
        }
        catch (JsonException)
        {
            try { File.Move(_path, _path + ".bak", overwrite: true); }
            catch (IOException) { /* 备份失败：保留原文件，仍回退默认配置 */ }
            catch (UnauthorizedAccessException) { /* Windows: .bak 被目录占用或不可写 */ }
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Config, JsonOptions.Default));
        File.Move(tmp, _path, overwrite: true);
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter ConfigStoreTests`
预期：6 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 配置模型与 ConfigStore（原子写/损坏恢复）"
```

---

## 任务 5：工作目录历史服务（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Config/WorkdirHistoryService.cs`
- 测试：`tests/ForgeDeck.Core.Tests/WorkdirHistoryTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/WorkdirHistoryTests.cs`：

```csharp
using ForgeDeck.Core.Config;

namespace ForgeDeck.Core.Tests;

public class WorkdirHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "forgedeck-tests", $"{Guid.NewGuid():N}.json");
    private readonly ConfigStore _store;

    public WorkdirHistoryTests() { _store = new ConfigStore(_path); _store.Load(); }
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Add_NewPath_Prepends()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a");
        service.Add(@"D:\b");
        Assert.Equal(new[] { @"D:\b", @"D:\a" }, service.List());
    }

    [Fact]
    public void Add_ExistingPath_MovesToFront_NoDuplicate()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a"); service.Add(@"D:\b"); service.Add(@"D:\a");
        Assert.Equal(new[] { @"D:\a", @"D:\b" }, service.List());
    }

    [Fact]
    public void Add_RespectsMaxHistory()
    {
        _store.Config.Settings.MaxWorkdirHistory = 3;
        var service = new WorkdirHistoryService(_store);
        for (var i = 1; i <= 5; i++) service.Add($@"D:\d{i}");
        Assert.Equal(new[] { @"D:\d5", @"D:\d4", @"D:\d3" }, service.List());
    }

    [Fact]
    public void Add_EmptyPath_Ignored()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add("   ");
        Assert.Empty(service.List());
    }

    [Fact]
    public void Add_PersistsToDisk()
    {
        new WorkdirHistoryService(_store).Add(@"D:\keep");
        var reloaded = new ConfigStore(_path);
        reloaded.Load();
        Assert.Contains(@"D:\keep", new WorkdirHistoryService(reloaded).List());
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a"); service.Add(@"D:\b");
        service.Remove(@"D:\a");
        Assert.Equal(new[] { @"D:\b" }, service.List());
    }

    [Fact]
    public void Add_PathCaseDifference_Deduped_KeepsLatestForm()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\P");
        service.Add(@"d:\p");
        Assert.Equal(new[] { @"d:\p" }, service.List());
    }

    [Fact]
    public void Remove_PathCaseDifference_DeletesEntry()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\Projects");
        service.Remove(@"d:\projects");
        Assert.Empty(service.List());
    }

    [Fact]
    public void Add_TrimsSurroundingWhitespace()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add("  D:\\a  ");
        Assert.Equal(new[] { @"D:\a" }, service.List());
    }

    [Fact]
    public void Add_MaxHistoryZero_ClampsToOne()
    {
        _store.Config.Settings.MaxWorkdirHistory = 0;
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\only");
        Assert.Equal(new[] { @"D:\only" }, service.List());
    }

    [Fact]
    public void Add_NullPath_Ignored_NoThrow()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(null!);
        Assert.Empty(service.List());
    }

    [Fact]
    public void List_ReturnsSnapshot_MutationDoesNotLeakIntoStore()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a");
        ((List<string>)service.List()).Add(@"D:\evil");
        Assert.Single(service.List());
    }

    [Fact]
    public void NullHistoryValue_ListAddRemove_AllSafe()
    {
        _store.Config.WorkdirHistory[WorkdirHistoryService.GlobalKey] = null!;
        var service = new WorkdirHistoryService(_store);
        Assert.Empty(service.List());
        service.Remove(@"D:\ghost"); // 不应抛异常
        service.Add(@"D:\a");        // 重建列表
        Assert.Equal(new[] { @"D:\a" }, service.List());
    }
}
```

注意：测试类里 `Path` 指 `System.IO.Path`（xUnit 项目默认 using System.IO；若命名冲突，文件头加 `using Path = System.IO.Path;`，与 ConfigStoreTests 相同处理）。

运行：`dotnet test --filter WorkdirHistoryTests` → 预期编译失败。

- [ ] **步骤 2：实现服务**

`src/ForgeDeck.Core/Config/WorkdirHistoryService.cs`：

```csharp
namespace ForgeDeck.Core.Config;

public sealed class WorkdirHistoryService(ConfigStore store)
{
    public const string GlobalKey = "__global__";

    public IReadOnlyList<string> List() =>
        store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) && list is not null
            ? list.ToList()
            : Array.Empty<string>();

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim();
        var list = Ensure();
        list.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        var max = Math.Max(1, store.Config.Settings.MaxWorkdirHistory);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        store.Save();
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) && list is not null
            && list.RemoveAll(x => string.Equals(x, path.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
            store.Save();
    }

    private List<string> Ensure()
    {
        if (!store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) || list is null)
        {
            list = new List<string>();
            store.Config.WorkdirHistory[GlobalKey] = list;
        }
        return list;
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter WorkdirHistoryTests` → 预期 13 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 全局工作目录历史（MRU/去重/上限）"
```

---

## 任务 6：已知工具目录、PATH 探测与扫描器骨架（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Scanning/KnownTools.cs`、`src/ForgeDeck.Core/Scanning/PathSearch.cs`、`src/ForgeDeck.Core/Scanning/IScanSource.cs`、`src/ForgeDeck.Core/Scanning/KnownDirsScanSource.cs`、`src/ForgeDeck.Core/Scanning/PathScanSource.cs`、`src/ForgeDeck.Core/Scanning/ToolScanner.cs`
- 测试：`tests/ForgeDeck.Core.Tests/ToolScannerTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/ToolScannerTests.cs`：

```csharp
using ForgeDeck.Core;
using ForgeDeck.Core.Scanning;

namespace ForgeDeck.Core.Tests;

public class ToolScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    public ToolScannerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string FakeExe(string name, string? sub = null)
    {
        var dir = sub == null ? _dir : Path.Combine(_dir, sub);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    private sealed class FakeSource(params ScanHit[] hits) : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) => hits;
    }

    private static readonly KnownTool Claude =
        new("Claude Code", ToolType.Cli, "C/", "--continue", new[] { "claude" }, Array.Empty<InstallHint>());

    [Fact]
    public void Scan_ReturnsHitTools_BuiltinFirst()
    {
        var claude = FakeExe("claude.cmd");
        var custom = FakeExe("mytool.exe");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(custom, null, "注册表")),
            new FakeSource(new ScanHit(claude, Claude, "npm 全局")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        Assert.Equal(2, tools.Count);
        Assert.Equal("Claude Code", tools[0].Name);       // builtin 排前
        Assert.True(tools[0].Builtin);
        Assert.Equal("mytool", tools[1].Name);
        Assert.False(tools[1].Builtin);
    }

    [Fact]
    public void Scan_SamePathFromTwoSources_KeepsFirst()
    {
        var claude = FakeExe("claude.cmd");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(claude, Claude, "npm 全局")),
            new FakeSource(new ScanHit(claude, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        var tool = Assert.Single(tools);
        Assert.Equal("npm 全局", tool.Source);
    }

    [Fact]
    public void Scan_SameKnownToolDifferentPaths_KeepsFirst()
    {
        var a = FakeExe("claude.cmd", "a");
        var b = FakeExe("claude.cmd", "b");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(a, Claude, "npm 全局"), new ScanHit(b, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        Assert.Single(tools);
        Assert.Equal(Path.GetFullPath(a), tools[0].ExePath);
    }

    [Fact]
    public void Scan_SkipsMissingFile()
    {
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(Path.Combine(_dir, "ghost.exe"), Claude, "PATH")),
        });
        Assert.Empty(scanner.Scan(new ScanContext(Array.Empty<string>())));
    }

    [Fact]
    public void KnownDirs_FindsToolInHintDir_WithExtraDirsFallback()
    {
        var hintDir = Path.Combine(_dir, "npm");
        Directory.CreateDirectory(hintDir);
        File.WriteAllText(Path.Combine(hintDir, "claude.cmd"), "");
        var extraDir = Path.Combine(_dir, "extra");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "codex.exe"), "");

        Environment.SetEnvironmentVariable("FD_TEST_NPM", hintDir);
        try
        {
            var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
                new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM%", "npm 全局") });
            var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
                new[] { "codex" }, Array.Empty<InstallHint>());
            var source = new KnownDirsScanSourceForTest(new[] { testTool, codexTool });

            var hits = source.Scan(new ScanContext(new[] { extraDir })).ToList();
            var claudeHit = Assert.Single(hits, h => h.Known!.Name == "Claude Code");
            Assert.Equal("npm 全局", claudeHit.SourceLabel);
            Assert.EndsWith("claude.cmd", claudeHit.ExePath);
            var codexHit = Assert.Single(hits, h => h.Known!.Name == "Codex CLI");
            Assert.Equal("附加目录", codexHit.SourceLabel);
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_NPM", null); }
    }
}

file sealed class KnownDirsScanSourceForTest(KnownTool[] tools) : KnownDirsScanSource
{
    protected override IEnumerable<KnownTool> Catalog => tools;
}
```

运行：`dotnet test --filter ToolScannerTests` → 预期编译失败。

- [ ] **步骤 2：实现扫描骨架**

`src/ForgeDeck.Core/Scanning/IScanSource.cs`：

```csharp
namespace ForgeDeck.Core.Scanning;

public sealed record ScanContext(IReadOnlyList<string> ExtraDirs);

public sealed record ScanHit(string ExePath, KnownTool? Known, string SourceLabel);

public interface IScanSource
{
    IEnumerable<ScanHit> Scan(ScanContext context);
}
```

`src/ForgeDeck.Core/Scanning/KnownTools.cs`：

```csharp
namespace ForgeDeck.Core.Scanning;

public sealed record InstallHint(string Pattern, string Label);

public sealed record KnownTool(
    string Name, ToolType Type, string Logo, string? ResumeArgs,
    string[] ExeNames, InstallHint[] Hints);

public static class KnownTools
{
    public static readonly IReadOnlyList<KnownTool> All = new KnownTool[]
    {
        new("Claude Code", ToolType.Cli, "C/", "--continue",
            new[] { "claude" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.claude\local", "用户目录") }),
        new("Codex CLI", ToolType.Cli, "CX", null,
            new[] { "codex" },
            new[] { new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录"),
                    new InstallHint(@"%APPDATA%\npm", "npm 全局") }),
        new("Gemini CLI", ToolType.Cli, "G", null,
            new[] { "gemini" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局") }),
        new("Aider", ToolType.Cli, "Ai", null,
            new[] { "aider" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Python\Scripts", "Python Scripts"),
                    new InstallHint(@"%APPDATA%\Python\Scripts", "Python Scripts") }),
        new("Cursor", ToolType.Gui, "Cu", null,
            new[] { "Cursor" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Cursor", "用户目录") }),
        new("Cursor Agent", ToolType.Cli, "Cu", null,
            new[] { "cursor-agent" },
            new[] { new InstallHint(@"%PROGRAMFILES%\Cursor\resources\app\bin", "开始菜单") }),
        new("Windsurf", ToolType.Gui, "W", null,
            new[] { "Windsurf" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Windsurf", "用户目录") }),
        new("Trae", ToolType.Gui, "T", null,
            new[] { "Trae" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Trae", "用户目录") }),
        new("Zed", ToolType.Gui, "Z", null,
            new[] { "zed", "Zed" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Zed", "用户目录") }),
        new("VS Code", ToolType.Gui, "VS", null,
            new[] { "Code" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Microsoft VS Code", "用户目录"),
                    new InstallHint(@"%PROGRAMFILES%\Microsoft VS Code", "用户目录") }),
    };

    public static KnownTool? MatchByExeName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return All.FirstOrDefault(t =>
            t.ExeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
    }

    public static KnownTool? MatchByName(string displayName) =>
        All.FirstOrDefault(t => displayName.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
}
```

`src/ForgeDeck.Core/Scanning/PathSearch.cs`：

```csharp
namespace ForgeDeck.Core.Scanning;

public static class PathSearch
{
    public static readonly string[] CliExtensions = { ".exe", ".cmd", ".bat", ".ps1" };

    public static IEnumerable<string> Probe(string dir, string name, string[]? extensions = null)
    {
        extensions ??= CliExtensions;
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) yield return direct;
        foreach (var ext in extensions)
        {
            var withExt = Path.Combine(dir, name + ext);
            if (File.Exists(withExt)) yield return withExt;
        }
    }

    public static IEnumerable<string> Probe(string dir, IEnumerable<string> names, string[]? extensions = null)
    {
        foreach (var name in names)
            foreach (var hit in Probe(dir, name, extensions))
                yield return hit;
    }

    public static IEnumerable<string> PathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? FindOnPath(string name)
    {
        foreach (var dir in PathDirectories())
            foreach (var hit in Probe(dir, name))
                return hit;
        return null;
    }
}
```

`src/ForgeDeck.Core/Scanning/KnownDirsScanSource.cs`（`Catalog` 虚属性供测试替换）：

```csharp
namespace ForgeDeck.Core.Scanning;

public class KnownDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInHints(tool) ?? FindInExtraDirs(tool, context.ExtraDirs);
            if (hit != null) yield return hit;
        }
    }

    private static ScanHit? FindInHints(KnownTool tool)
    {
        foreach (var hint in tool.Hints)
        {
            var dir = Environment.ExpandEnvironmentVariables(hint.Pattern);
            if (!Directory.Exists(dir)) continue;
            var path = PathSearch.Probe(dir, tool.ExeNames).FirstOrDefault();
            if (path != null) return new ScanHit(Path.GetFullPath(path), tool, hint.Label);
        }
        return null;
    }

    private static ScanHit? FindInExtraDirs(KnownTool tool, IReadOnlyList<string> extraDirs)
    {
        foreach (var extra in extraDirs)
        {
            if (string.IsNullOrWhiteSpace(extra) || !Directory.Exists(extra)) continue;
            var path = PathSearch.Probe(extra, tool.ExeNames).FirstOrDefault();
            if (path != null) return new ScanHit(Path.GetFullPath(path), tool, "附加目录");
        }
        return null;
    }
}
```

`src/ForgeDeck.Core/Scanning/PathScanSource.cs`：

```csharp
namespace ForgeDeck.Core.Scanning;

public sealed class PathScanSource : IScanSource
{
    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in KnownTools.All)
        {
            var hit = FindTool(tool);
            if (hit != null) yield return hit;
        }
    }

    internal static ScanHit? FindTool(KnownTool tool)
    {
        foreach (var exe in tool.ExeNames)
            foreach (var dir in PathSearch.PathDirectories())
                foreach (var path in PathSearch.Probe(dir, exe))
                    return new ScanHit(Path.GetFullPath(path), tool, "PATH");
        return null;
    }
}
```

`src/ForgeDeck.Core/Scanning/ToolScanner.cs`：

```csharp
namespace ForgeDeck.Core.Scanning;

public sealed class ToolScanner
{
    private readonly IEnumerable<IScanSource> _sources;

    public ToolScanner(IEnumerable<IScanSource> sources) => _sources = sources;

    public List<ToolInfo> Scan(ScanContext context)
    {
        var byPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
            foreach (var hit in source.Scan(context))
            {
                if (!File.Exists(hit.ExePath)) continue;
                var path = Path.GetFullPath(hit.ExePath);
                if (byPath.ContainsKey(path)) continue;
                var name = hit.Known?.Name ?? Path.GetFileNameWithoutExtension(path);
                if (hit.Known != null && !seenNames.Add(name)) continue;
                byPath[path] = new ToolInfo
                {
                    Name = name,
                    Type = hit.Known?.Type ?? ToolType.Cli,
                    ExePath = path,
                    Source = hit.SourceLabel,
                    Builtin = hit.Known != null,
                };
            }
        return byPath.Values
            .OrderByDescending(t => t.Builtin)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter ToolScannerTests` → 预期 5 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 已知工具目录、PATH/目录探测与扫描聚合（去重/优先级）"
```

---

## 任务 7：注册表与开始菜单扫描源（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Scanning/RegistryScanSource.cs`、`src/ForgeDeck.Core/Scanning/StartMenuScanSource.cs`
- 测试：`tests/ForgeDeck.Core.Tests/RegistryAndStartMenuTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/RegistryAndStartMenuTests.cs`：

```csharp
using ForgeDeck.Core.Scanning;
using Microsoft.Win32;

namespace ForgeDeck.Core.Tests;

public class RegistryAndStartMenuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private const string TestUninstallKey = @"Software\ForgeDeckTests\Uninstall";

    public RegistryAndStartMenuTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ForgeDeckTests", false); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RegistrySource_MatchesKnownTool_ByDisplayNameAndIcon()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorApp"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", $"{exe},0");
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hits = source.Scan(new ScanContext(Array.Empty<string>())).ToList();
        var hit = Assert.Single(hits);
        Assert.Equal("Cursor", hit.Known!.Name);
        Assert.Equal("注册表", hit.SourceLabel);
        Assert.Equal(Path.GetFullPath(exe), hit.ExePath);
    }

    [Fact]
    public void RegistrySource_SkipsUnrelatedEntries()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\RandomApp"))
        {
            key.SetValue("DisplayName", "Some Random Software");
        }
        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        Assert.Empty(source.Scan(new ScanContext(Array.Empty<string>())));
    }

    [Fact]
    public void StartMenuSource_ResolvesLnkTarget()
    {
        var exe = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(exe, "");
        var lnkPath = Path.Combine(_dir, "Claude.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = exe;
        shortcut.Save();

        var resolver = new WScriptShellLinkResolver();
        Assert.Equal(exe, resolver.ResolveTarget(lnkPath));

        // 目录级验证：把 .lnk 放进伪造的开始菜单目录
        var menuDir = Path.Combine(_dir, "StartMenu");
        Directory.CreateDirectory(menuDir);
        var lnk2 = Path.Combine(menuDir, "Claude2.lnk");
        dynamic sc2 = shell.CreateShortcut(lnk2);
        sc2.TargetPath = exe;
        sc2.Save();
        var source = new StartMenuScanSourceForTest(resolver, new[] { menuDir });
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
        Assert.Equal("Claude Code", hit.Known!.Name);
        Assert.Equal("开始菜单", hit.SourceLabel);
    }
}

file sealed class StartMenuScanSourceForTest(IShellLinkResolver resolver, string[] dirs)
    : StartMenuScanSource(resolver)
{
    protected override string[] MenuDirs => dirs;
}
```

运行：`dotnet test --filter RegistryAndStartMenuTests` → 预期编译失败。

- [ ] **步骤 2：实现两个扫描源**

`src/ForgeDeck.Core/Scanning/RegistryScanSource.cs`：

```csharp
using Microsoft.Win32;

namespace ForgeDeck.Core.Scanning;

public sealed record RegistryEntry(string DisplayName, string InstallLocation, string DisplayIcon);

public interface IUninstallRegistry
{
    IEnumerable<RegistryEntry> Entries();
}

public sealed class RegistryUninstallRegistry : IUninstallRegistry
{
    private static readonly string[] DefaultKeyPaths =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private readonly string[] _keyPaths;

    public RegistryUninstallRegistry() : this(DefaultKeyPaths) { }
    public RegistryUninstallRegistry(string[] keyPaths) => _keyPaths = keyPaths;

    public IEnumerable<RegistryEntry> Entries()
    {
        foreach (var keyPath in _keyPaths)
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey(keyPath);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var item = key.OpenSubKey(sub);
                    if (item == null) continue;
                    yield return new RegistryEntry(
                        (string?)item.GetValue("DisplayName") ?? "",
                        (string?)item.GetValue("InstallLocation") ?? "",
                        (string?)item.GetValue("DisplayIcon") ?? "");
                }
            }
    }
}

public sealed class RegistryScanSource(IUninstallRegistry registry) : IScanSource
{
    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var entry in registry.Entries())
        {
            if (entry.DisplayName.Length == 0) continue;
            var known = KnownTools.MatchByName(entry.DisplayName);
            if (known == null) continue;
            var exe = ResolveExe(entry, known);
            if (exe != null) yield return new ScanHit(exe, known, "注册表");
        }
    }

    private static string? ResolveExe(RegistryEntry entry, KnownTool known)
    {
        var icon = entry.DisplayIcon.Split(',')[0].Trim().Trim('"');
        if (icon.Length > 0 && File.Exists(icon)
            && KnownTools.MatchByExeName(icon)?.Name == known.Name)
            return Path.GetFullPath(icon);

        if (entry.InstallLocation.Length > 0 && Directory.Exists(entry.InstallLocation))
        {
            var probed = PathSearch.Probe(entry.InstallLocation, known.ExeNames).FirstOrDefault();
            if (probed != null) return Path.GetFullPath(probed);
        }
        return null;
    }
}
```

`src/ForgeDeck.Core/Scanning/StartMenuScanSource.cs`：

```csharp
using System.Runtime.InteropServices;

namespace ForgeDeck.Core.Scanning;

public interface IShellLinkResolver
{
    string? ResolveTarget(string lnkPath);
}

public sealed class WScriptShellLinkResolver : IShellLinkResolver
{
    public string? ResolveTarget(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            var target = (string)shortcut.TargetPath;
            return target.Length > 0 ? target : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}

public class StartMenuScanSource(IShellLinkResolver resolver) : IScanSource
{
    protected virtual string[] MenuDirs => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
    };

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var dir in MenuDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var target = resolver.ResolveTarget(lnk);
                if (target == null || !File.Exists(target)) continue;
                var known = KnownTools.MatchByExeName(target);
                if (known == null) continue;
                yield return new ScanHit(Path.GetFullPath(target), known, "开始菜单");
            }
        }
    }
}
```

测试项目需要 COM 互运用 dynamic：`tests/ForgeDeck.Core.Tests/ForgeDeck.Core.Tests.csproj` 确认含 `<UseRidSourceGenerate>false</UseRidSourceGenerate>` 不需要；`dynamic` 在 net8.0 开箱可用，无需额外包。

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter RegistryAndStartMenuTests` → 预期 3 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 注册表卸载项与开始菜单快捷方式扫描源"
```

---

## 任务 8：启动服务（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Launching/LaunchService.cs`
- 测试：`tests/ForgeDeck.Core.Tests/LaunchServiceTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/LaunchServiceTests.cs`：

```csharp
using ForgeDeck.Core;
using ForgeDeck.Core.Launching;

namespace ForgeDeck.Core.Tests;

public class LaunchServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private readonly LaunchService _service = new();

    public LaunchServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ToolInfo Tool(string exe) => new() { Name = "T", ExePath = exe };
    private static LaunchProfile Profile(string args = "", string workdir = "", bool autoRestore = false) =>
        new() { ToolId = "t", Args = args, Workdir = workdir, AutoRestore = autoRestore };

    [Theory]
    [InlineData(@"--model ""sonnet 4"" --x", new[] { "--model", "sonnet 4", "--x" })]
    [InlineData("", new string[0])]
    [InlineData("  --a   --b  ", new[] { "--a", "--b" })]
    [InlineData("'quoted arg'", new[] { "quoted arg" })]
    public void SplitArgs_HandlesQuotesAndWhitespace(string input, string[] expected)
    {
        Assert.Equal(expected, LaunchService.SplitArgs(input));
    }

    [Fact]
    public void BuildCommand_Exe_RunsDirectly()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        var cmd = _service.BuildCommand(Tool(exe), Profile("--verbose"));
        Assert.Equal(exe, cmd.App);
        Assert.Equal(new[] { "--verbose" }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_CmdScript_WrapsWithCmdC()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var cmd = _service.BuildCommand(Tool(script), Profile("--model x"));
        Assert.EndsWith("cmd.exe", cmd.App);
        Assert.Equal(new[] { "/c", script, "--model", "x" }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_Ps1_WrapsWithPwshOrPowershell()
    {
        var script = Path.Combine(_dir, "tool.ps1");
        File.WriteAllText(script, "");
        var cmd = _service.BuildCommand(Tool(script), Profile());
        Assert.True(cmd.App.Contains("pwsh") || cmd.App.Contains("powershell"), $"实际 App: {cmd.App}");
        Assert.Equal(new[] { "-File", script }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_ClaudeAutoRestore_AppendsResumeArgs()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var withRestore = _service.BuildCommand(Tool(script), Profile("--model x", autoRestore: true));
        Assert.Contains("--continue", withRestore.Args);
        var alreadyHas = _service.BuildCommand(Tool(script), Profile("--continue", autoRestore: true));
        Assert.Single(alreadyHas.Args.Where(a => a == "--continue"));
    }

    [Fact]
    public void Validate_MissingExe_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.Validate(Tool(Path.Combine(_dir, "ghost.exe")), Profile(workdir: _dir)));
        Assert.Contains("可执行文件不存在", ex.Message);
    }

    [Fact]
    public void Validate_MissingWorkdir_Throws()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        Assert.Throws<InvalidOperationException>(
            () => _service.Validate(Tool(exe), Profile(workdir: Path.Combine(_dir, "nope"))));
    }

    [Fact]
    public void Validate_EmptyWorkdir_FallsBackToHome()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        _service.Validate(Tool(exe), Profile()); // 不抛即通过
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            LaunchService.ResolveWorkdir(Profile()));
    }

    [Fact]
    public void ResolveEnv_ExpandsVariables_AndSkipsEmptyKeys()
    {
        try
        {
            Environment.SetEnvironmentVariable("FD_TEST_VAR", "hello");
            var profile = Profile();
            profile.Env["A"] = "%FD_TEST_VAR% world";
            profile.Env[" "] = "skip";
            var env = _service.ResolveEnv(profile);
            Assert.Equal("hello world", env["A"]);
            Assert.False(env.ContainsKey(" "));
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_VAR", null); }
    }

    [Fact]
    public void BuildExternalStartInfo_UsesRawArgsAndEnv()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        var profile = Profile("--model \"sonnet 4\"", _dir);
        profile.Env["K"] = "V";
        var psi = _service.BuildExternalStartInfo(Tool(exe), profile);
        Assert.Equal(exe, psi.FileName);
        Assert.Equal("--model \"sonnet 4\"", psi.Arguments);   // 外部启动保留原始串
        Assert.Equal(_dir, psi.WorkingDirectory);
        Assert.Equal("V", psi.EnvironmentVariables["K"]);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void LaunchExternal_CmdExitsWithCode()
    {
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var tool = Tool(cmdPath);
        var profile = Profile("/c exit 3", _dir);
        using var process = Process.Start(_service.BuildExternalStartInfo(tool, profile))!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(3, process.ExitCode);
    }
}
```

运行：`dotnet test --filter LaunchServiceTests` → 预期编译失败。

- [ ] **步骤 2：实现 LaunchService**

`src/ForgeDeck.Core/Launching/LaunchService.cs`：

```csharp
using System.Text;
using ForgeDeck.Core.Scanning;

namespace ForgeDeck.Core.Launching;

public sealed record LaunchCommand(string App, IReadOnlyList<string> Args);

public sealed class LaunchService
{
    /// <summary>引号感知的参数分词（支持 "..." 与 '...'，引号内保留空白）。</summary>
    public static IReadOnlyList<string> SplitArgs(string args)
    {
        var result = new List<string>();
        var i = 0;
        while (i < args.Length)
        {
            while (i < args.Length && char.IsWhiteSpace(args[i])) i++;
            if (i >= args.Length) break;
            var sb = new StringBuilder();
            while (i < args.Length && !char.IsWhiteSpace(args[i]))
            {
                var c = args[i];
                if (c is '"' or '\'')
                {
                    var quote = c;
                    i++;
                    while (i < args.Length && args[i] != quote) sb.Append(args[i++]);
                    i++; // 跳过闭合引号
                }
                else sb.Append(args[i++]);
            }
            result.Add(sb.ToString());
        }
        return result;
    }

    public LaunchCommand BuildCommand(ToolInfo tool, LaunchProfile profile)
    {
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        var args = SplitArgs(profile.Args).ToList();
        var known = KnownTools.MatchByExeName(tool.ExePath);
        if (profile.AutoRestore && known?.ResumeArgs is { } resume && !args.Contains(resume))
            args.Add(resume);
        return ext switch
        {
            ".exe" => new LaunchCommand(tool.ExePath, args),
            ".cmd" or ".bat" => new LaunchCommand(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                new[] { "/c", tool.ExePath }.Concat(args).ToList()),
            ".ps1" => new LaunchCommand(
                PathSearch.FindOnPath("pwsh") ?? "powershell.exe",
                new[] { "-File", tool.ExePath }.Concat(args).ToList()),
            _ => throw new NotSupportedException($"不支持的启动文件类型：{ext}"),
        };
    }

    public void Validate(ToolInfo tool, LaunchProfile profile)
    {
        if (!File.Exists(tool.ExePath))
            throw new InvalidOperationException($"可执行文件不存在：{tool.ExePath}");
        var workdir = ResolveWorkdir(profile);
        if (!Directory.Exists(workdir))
            throw new InvalidOperationException($"工作目录不存在：{workdir}");
    }

    public static string ResolveWorkdir(LaunchProfile profile) =>
        profile.Workdir.Trim().Length == 0
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.ExpandEnvironmentVariables(profile.Workdir.Trim());

    public IReadOnlyDictionary<string, string> ResolveEnv(LaunchProfile profile)
    {
        var env = new Dictionary<string, string>();
        foreach (var (key, value) in profile.Env)
        {
            if (key.Trim().Length == 0) continue;
            env[key.Trim()] = Environment.ExpandEnvironmentVariables(value);
        }
        return env;
    }

    public ProcessStartInfo BuildExternalStartInfo(ToolInfo tool, LaunchProfile profile)
    {
        Validate(tool, profile);
        var psi = new ProcessStartInfo
        {
            FileName = tool.ExePath,
            Arguments = profile.Args,
            WorkingDirectory = ResolveWorkdir(profile),
            UseShellExecute = false,
        };
        foreach (var (key, value) in ResolveEnv(profile))
            psi.EnvironmentVariables[key] = value;
        return psi;
    }

    public int LaunchExternal(ToolInfo tool, LaunchProfile profile)
    {
        using var process = Process.Start(BuildExternalStartInfo(tool, profile))
            ?? throw new InvalidOperationException("进程启动失败");
        return process.Id;
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter LaunchServiceTests` → 预期全部 Passed（含 4 条 InlineData）。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 启动服务——校验/命令包装/env 展开/外部启动"
```

---

## 任务 9：终端会话管理器（ConPTY，集成测试）

**文件：**
- 创建：`src/ForgeDeck.Core/Terminal/TerminalSessionManager.cs`
- 测试：`tests/ForgeDeck.Core.Tests/TerminalSessionManagerTests.cs`

- [ ] **步骤 1：编写失败的集成测试**

`tests/ForgeDeck.Core.Tests/TerminalSessionManagerTests.cs`：

```csharp
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Tests;

public class TerminalSessionManagerTests : IDisposable
{
    private static readonly string CmdExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    private readonly TerminalSessionManager _mgr = new();

    public void Dispose() => _mgr.Dispose();

    private static async Task<string> WaitForOutputAsync(
        TerminalSessionManager mgr, string sessionId, Func<string, bool> done, TimeSpan timeout)
    {
        var acc = "";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOutput(string id, string chunk)
        {
            if (id != sessionId) return;
            acc += chunk;
            if (done(acc)) tcs.TrySetResult(acc);
        }
        mgr.Output += OnOutput;
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            Assert.True(tcs.Task.IsCompleted, $"超时未收到预期输出，当前输出：{acc}");
            return acc;
        }
        finally { mgr.Output -= OnOutput; }
    }

    private static Task<int> WaitForExitAsync(TerminalSessionManager mgr, string sessionId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExit(string id, int code) { if (id == sessionId) tcs.TrySetResult(code); }
        mgr.Exited += OnExit;
        return Task.WhenAny(tcs.Task, Task.Delay(timeout)).ContinueWith(_ =>
        {
            mgr.Exited -= OnExit;
            Assert.True(tcs.Task.IsCompleted, "超时未收到退出事件");
            return tcs.Task.Result;
        });
    }

    [Fact]
    public async Task Create_CmdEcho_CapturesOutputAndExit()
    {
        var id = await _mgr.CreateAsync("echo", CmdExe, new[] { "/c", "echo forgedeck-ok" }, Path.GetTempPath());
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forgedeck-ok"), TimeSpan.FromSeconds(10));
        var code = await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
        Assert.Equal(0, code);
        var session = Assert.Single(_mgr.List());
        Assert.False(session.Running);
        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public async Task Write_InteractiveCmd_EchoesInput()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600); // 等 shell 就绪
        _mgr.Write(id, "echo forge-input-test\r");
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forge-input-test"), TimeSpan.FromSeconds(10));
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Resize_DoesNotThrow()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        _mgr.Resize(id, 100, 30);
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Close_RemovesSessionFromList()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        _mgr.Close(id);
        Assert.Empty(_mgr.List());
    }

    [Fact]
    public async Task HasRunningSessions_ReflectsState()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        Assert.True(_mgr.HasRunningSessions);
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
        Assert.False(_mgr.HasRunningSessions);
    }
}
```

运行：`dotnet test --filter TerminalSessionManagerTests` → 预期编译失败。

- [ ] **步骤 2：实现会话管理器**

`src/ForgeDeck.Core/Terminal/TerminalSessionManager.cs`：

```csharp
using System.Collections;
using System.Text;
using Porta.Pty;

namespace ForgeDeck.Core.Terminal;

public sealed record TerminalSessionInfo(string SessionId, string Title, string Workdir, bool Running, int? ExitCode);

public sealed class TerminalSessionManager : IDisposable
{
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly object _gate = new();

    /// <summary>终端输出（sessionId, chunk，UTF-8 已解码）。</summary>
    public event Action<string, string>? Output;
    /// <summary>进程退出（sessionId, exitCode）。</summary>
    public event Action<string, int>? Exited;
    /// <summary>会话列表或运行状态变化。</summary>
    public event Action? Changed;

    public async Task<string> CreateAsync(
        string title, string app, IReadOnlyList<string> args, string workdir,
        IReadOnlyDictionary<string, string>? env = null, int cols = 120, int rows = 30)
    {
        // 合并全量环境变量，避免子进程丢 PATH 等基础变量
        var merged = new Dictionary<string, string>();
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string)e.Value!;
        if (env != null)
            foreach (var (key, value) in env)
                merged[key] = value;

        var id = Guid.NewGuid().ToString("N");
        var connection = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = title,
            Cols = cols,
            Rows = rows,
            Cwd = workdir,
            App = app,
            CommandLine = args.ToArray(),
            Environment = merged,
        }, CancellationToken.None);

        var session = new Session(id, title, workdir, connection);
        lock (_gate) { _sessions[id] = session; }
        connection.ProcessExited += (_, e) =>
        {
            session.Running = false;
            session.ExitCode = e.ExitCode;
            Exited?.Invoke(id, e.ExitCode);
            Changed?.Invoke();
        };
        _ = PumpOutputAsync(session);
        Changed?.Invoke();
        return id;
    }

    private async Task PumpOutputAsync(Session session)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await session.Connection.ReaderStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read <= 0) break;
                Output?.Invoke(session.Id, Encoding.UTF8.GetString(buffer, 0, read));
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    public void Write(string sessionId, string data)
    {
        var session = Get(sessionId);
        var bytes = Encoding.UTF8.GetBytes(data);
        session.Connection.WriterStream.Write(bytes, 0, bytes.Length);
        session.Connection.WriterStream.Flush();
    }

    public void Resize(string sessionId, int cols, int rows) => Get(sessionId).Connection.Resize(cols, rows);

    public void Kill(string sessionId)
    {
        Session? session;
        lock (_gate) { _sessions.TryGetValue(sessionId, out session); }
        if (session == null || !session.Running) return;
        try { session.Connection.Kill(); } catch { }
    }

    /// <summary>关闭并从列表移除会话（标签页 × 按钮）。</summary>
    public void Close(string sessionId)
    {
        Session? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session)) return;
            _sessions.Remove(sessionId);
        }
        try { if (session.Running) session.Connection.Kill(); } catch { }
        session.Dispose();
        Changed?.Invoke();
    }

    public void KillAll()
    {
        List<Session> running;
        lock (_gate) running = _sessions.Values.Where(s => s.Running).ToList();
        foreach (var session in running)
            try { session.Connection.Kill(); } catch { }
    }

    public bool HasRunningSessions
    {
        get { lock (_gate) return _sessions.Values.Any(s => s.Running); }
    }

    public IReadOnlyList<TerminalSessionInfo> List()
    {
        lock (_gate)
            return _sessions.Values
                .OrderBy(s => s.StartedAt)
                .Select(s => new TerminalSessionInfo(s.Id, s.Title, s.Workdir, s.Running, s.Running ? null : s.ExitCode))
                .ToList();
    }

    private Session Get(string sessionId)
    {
        lock (_gate)
            return _sessions.TryGetValue(sessionId, out var s)
                ? s : throw new KeyNotFoundException($"会话不存在：{sessionId}");
    }

    public void Dispose()
    {
        List<Session> all;
        lock (_gate)
        {
            all = _sessions.Values.ToList();
            _sessions.Clear();
        }
        foreach (var s in all) s.Dispose();
    }

    private sealed class Session(string id, string title, string workdir, IPtyConnection connection) : IDisposable
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Workdir { get; } = workdir;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public IPtyConnection Connection { get; } = connection;
        public bool Running { get; set; } = true;
        public int ExitCode { get; set; } = -1;
        public void Dispose() => Connection.Dispose();
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter TerminalSessionManagerTests` → 预期 5 Passed（约 5-10 秒，含真实 ConPTY 进程）。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): ConPTY 终端会话管理器（输出流/输入/resize/kill/close）"
```

---

## 任务 10：消息桥——分发器与业务接线（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Bridge/BridgeException.cs`、`src/ForgeDeck.Core/Bridge/BridgeDispatcher.cs`、`src/ForgeDeck.Core/Bridge/ForgeDeckBridge.cs`
- 测试：`tests/ForgeDeck.Core.Tests/BridgeTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/BridgeTests.cs`：

```csharp
using System.Text.Json;
using ForgeDeck.Core;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Tests;

public class BridgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private readonly ConfigStore _store = null!;
    private readonly ForgeDeckBridge _bridge = null!;

    public BridgeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[] { new EmptySource() }),
            new TerminalSessionManager());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class EmptySource : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) { yield break; }
    }

    private static JsonElement ResultOf(string response)
    {
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.GetProperty("result");
    }

    private static (string Code, string Message)? ErrorOf(string response)
    {
        using var doc = JsonDocument.Parse(response);
        if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
        return (err.GetProperty("code").GetString()!, err.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("{ broken");
        Assert.NotNull(resp);
        Assert.Equal(("-32700", "请求不是合法 JSON"), ErrorOf(resp!));
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":1,"method":"nope.nope"}""");
        Assert.NotNull(resp);
        var (code, _) = ErrorOf(resp!)!.Value;
        Assert.Equal("-32601", code);
    }

    [Fact]
    public async Task AppInfo_ReturnsVersionAndUser()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":2,"method":"app.info"}""");
        var result = ResultOf(resp!);
        Assert.Equal("0.1.0", result.GetProperty("version").GetString());
        Assert.Equal(Environment.UserName, result.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task AddManual_InvalidPath_ReturnsValidationError()
    {
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$"""{"id":3,"method":"tools.addManual","params":{"name":"X","exePath":"{{Path.Combine(_dir, "ghost.exe").Replace("\\", "\\\\")}}"}}""");
        var (code, message) = ErrorOf(resp!)!.Value;
        Assert.Equal("validation", code);
        Assert.Contains("可执行文件不存在", message);
    }

    [Fact]
    public async Task AddManual_Valid_AddsToolAndPersists()
    {
        var exe = Path.Combine(_dir, "mytool.exe");
        File.WriteAllText(exe, "");
        var exeJson = exe.Replace("\\", "\\\\");
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$"""{"id":4,"method":"tools.addManual","params":{"name":"MyTool","exePath":"{{exeJson}}"}}""");
        var result = ResultOf(resp!);
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal("MyTool", result[0].GetProperty("tool").GetProperty("name").GetString());

        var reloaded = new ConfigStore(Path.Combine(_dir, "config.json"));
        reloaded.Load();
        Assert.Contains(reloaded.Config.Tools, t => t.Name == "MyTool" && t.Manual);
    }

    [Fact]
    public async Task ProfilesGet_Missing_ReturnsDefaultWithPreferEmbedded()
    {
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":5,"method":"profiles.get","params":{"toolId":"t1"}}""");
        var result = ResultOf(resp!);
        Assert.Equal("embedded", result.GetProperty("openMode").GetString());
        Assert.Equal("t1", result.GetProperty("toolId").GetString());
    }

    [Fact]
    public async Task ProfilesSave_ThenGet_ReturnsSaved()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":6,"method":"profiles.save","params":{"profile":{"id":"p1","toolId":"t1","name":"默认","args":"--x","env":{"K":"V"},"workdir":"","openMode":"external","autoRestore":false}}}""");
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":7,"method":"profiles.get","params":{"toolId":"t1"}}""");
        var result = ResultOf(resp!);
        Assert.Equal("p1", result.GetProperty("id").GetString());
        Assert.Equal("external", result.GetProperty("openMode").GetString());
    }

    [Fact]
    public async Task TerminalCreate_WithCmdTool_CreatesSession()
    {
        var cmdScript = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(cmdScript, "@echo off\r\necho forge-bridge-e2e\r\n");
        _store.Config.Tools.Add(new ToolInfo { Id = "tc1", Name = "Fake Claude", ExePath = cmdScript, Source = "测试" });
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":8,"method":"terminal.create","params":{"toolId":"tc1","cols":80,"rows":24}}""");
        var sessionId = ResultOf(resp!).GetProperty("sessionId").GetString();
        Assert.NotNull(sessionId);

        var listResp = await _bridge.Dispatcher.HandleAsync("""{"id":9,"method":"sessions.list"}""");
        Assert.Equal(1, ResultOf(listResp!).GetArrayLength());
        // lastUsed 与工作目录历史联动
        Assert.NotNull(_store.Config.LastUsed);
        Assert.Equal("tc1", _store.Config.LastUsed!.ToolId);
    }

    [Fact]
    public async Task TerminalWrite_UnknownSession_ReturnsInternalError()
    {
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":10,"method":"terminal.write","params":{"sessionId":"nope","data":"x"}}""");
        var (code, _) = ErrorOf(resp!)!.Value;
        Assert.Equal("internal", code);
    }

    [Fact]
    public async Task SettingsGetSave_RoundTrip()
    {
        var getResp = await _bridge.Dispatcher.HandleAsync("""{"id":11,"method":"settings.get"}""");
        var result = ResultOf(getResp!);
        Assert.True(result.GetProperty("commonDirs").GetArrayLength() > 0);

        var saveResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":12,"method":"settings.save","params":{"settings":{"defaultShell":"cmd","autoScanOnStartup":false,"extraScanDirs":["D:\\Tools"],"skipExitConfirm":true,"preferEmbedded":false,"maxWorkdirHistory":20}}}""");
        Assert.Equal("cmd", ResultOf(saveResp!).GetProperty("settings").GetProperty("defaultShell").GetString());
        Assert.False(_store.Config.Settings.AutoScanOnStartup);
        Assert.True(_store.Config.Settings.SkipExitConfirm);
    }

    [Fact]
    public async Task Workdirs_AddAndList()
    {
        await _bridge.Dispatcher.HandleAsync(
            $$"""{"id":13,"method":"workdirs.add","params":{"path":"{{_dir.Replace("\\", "\\\\")}}"}}""");
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":14,"method":"workdirs.list"}""");
        Assert.Equal(_dir, ResultOf(resp!)[0].GetString());
    }
}
```

运行：`dotnet test --filter BridgeTests` → 预期编译失败。

- [ ] **步骤 2：实现分发器与业务桥**

`src/ForgeDeck.Core/Bridge/BridgeException.cs`：

```csharp
namespace ForgeDeck.Core.Bridge;

public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
```

`src/ForgeDeck.Core/Bridge/BridgeDispatcher.cs`：

```csharp
using System.Text;
using System.Text.Json;

namespace ForgeDeck.Core.Bridge;

public sealed class BridgeDispatcher
{
    private static readonly JsonSerializerOptions Opts = JsonOptions.Create(o => o.WriteIndented = false);
    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();

    /// <summary>需要推送给前端的消息（响应走返回值，事件走这里）。</summary>
    public event Action<string>? Outgoing;

    public void Register(string method, Func<JsonElement?, Task<object?>> handler) =>
        _handlers[method] = handler;

    public void Emit(string eventName, object data) =>
        Outgoing?.Invoke(JsonSerializer.Serialize(new { @event = eventName, data }, Opts));

    public async Task<string?> HandleAsync(string json)
    {
        JsonElement? id = null;
        string? method = null;
        JsonElement? parameters = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, "-32600", "请求必须是 JSON 对象");
            if (root.TryGetProperty("id", out var idEl)) id = idEl.Clone();
            if (root.TryGetProperty("method", out var mEl)) method = mEl.GetString();
            if (root.TryGetProperty("params", out var pEl) && pEl.ValueKind != JsonValueKind.Null)
                parameters = pEl.Clone();
        }
        catch (JsonException)
        {
            return Error(id, "-32700", "请求不是合法 JSON");
        }
        if (string.IsNullOrEmpty(method)) return Error(id, "-32602", "缺少 method");
        if (!_handlers.TryGetValue(method!, out var handler))
            return Error(id, "-32601", $"未知方法：{method}");

        object? result;
        try { result = await handler(parameters); }
        catch (BridgeException ex) { return Error(id, ex.Code, ex.Message); }
        catch (Exception ex) { return Error(id, "internal", ex.Message); }
        if (id == null) return null;

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            id.Value.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, Opts);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Error(JsonElement? id, string code, string message)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            if (id != null)
            {
                writer.WritePropertyName("id");
                id.Value.WriteTo(writer);
            }
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
```

`src/ForgeDeck.Core/Bridge/ForgeDeckBridge.cs`：

```csharp
using System.Text.Json;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Launching;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Bridge;

public sealed record ToolListItem(ToolInfo Tool, bool Exists, OpenMode DefaultMode);
public sealed record CommonDir(string Name, string Path);
public sealed record AppInfo(string Version, string UserName, DateTime? LastScanAt, LastUsedInfo? LastUsed);
public sealed record SettingsInfo(AppSettings Settings, IReadOnlyList<CommonDir> CommonDirs, string UserName);

public sealed class ForgeDeckBridge
{
    public const string Version = "0.1.0";

    private readonly ConfigStore _store;
    private readonly ToolScanner _scanner;
    private readonly TerminalSessionManager _terminal;
    private readonly LaunchService _launch = new();
    private readonly WorkdirHistoryService _workdirs;

    public BridgeDispatcher Dispatcher { get; }

    public ForgeDeckBridge(ConfigStore store, ToolScanner scanner, TerminalSessionManager terminal)
    {
        _store = store;
        _scanner = scanner;
        _terminal = terminal;
        _workdirs = new WorkdirHistoryService(store);
        Dispatcher = new BridgeDispatcher();
        RegisterMethods();
        _terminal.Output += (id, chunk) => Dispatcher.Emit("terminal.data", new { sessionId = id, chunk });
        _terminal.Exited += (id, code) => Dispatcher.Emit("terminal.exit", new { sessionId = id, exitCode = code });
        _terminal.Changed += () => Dispatcher.Emit("sessions.changed", new { });
    }

    private void RegisterMethods()
    {
        Dispatcher.Register("app.info", _ =>
            Task.FromResult<object?>(new AppInfo(Version, Environment.UserName, _store.Config.LastScanAt, _store.Config.LastUsed)));

        Dispatcher.Register("tools.list", _ => Task.FromResult<object?>(ToolsList()));

        Dispatcher.Register("tools.rescan", _ =>
        {
            var found = _scanner.Scan(new ScanContext(_store.Config.Settings.ExtraScanDirs));
            var manual = _store.Config.Tools.Where(t => t.Manual).ToList();
            _store.Config.Tools = manual.Concat(found).ToList();
            _store.Config.LastScanAt = DateTime.UtcNow;
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("tools.addManual", p =>
        {
            var name = p?.GetProperty("name").GetString() ?? "";
            var exePath = p?.GetProperty("exePath").GetString() ?? "";
            if (name.Trim().Length == 0) throw new BridgeException("validation", "工具名称不能为空");
            if (!File.Exists(exePath)) throw new BridgeException("validation", $"可执行文件不存在：{exePath}");
            _store.Config.Tools.Add(new ToolInfo
            {
                Name = name.Trim(),
                ExePath = Path.GetFullPath(exePath),
                Source = "手动添加",
                Manual = true,
            });
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("profiles.get", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var profile = _store.Config.Profiles.FirstOrDefault(x => x.ToolId == toolId)
                          ?? DefaultProfile(toolId);
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.save", p =>
        {
            var profile = p?.GetProperty("profile").Deserialize<LaunchProfile>(JsonOptions.Create(o => o.WriteIndented = false))
                ?? throw new BridgeException("validation", "无效的配置");
            _store.Config.Profiles.RemoveAll(x => x.Id == profile.Id || x.ToolId == profile.ToolId);
            _store.Config.Profiles.Add(profile);
            _store.Save();
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.delete", p =>
        {
            var id = p?.GetProperty("id").GetString() ?? "";
            _store.Config.Profiles.RemoveAll(x => x.Id == id);
            _store.Save();
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("settings.get", _ => Task.FromResult<object?>(new SettingsInfo(
            _store.Config.Settings, CommonDirs(), Environment.UserName)));

        Dispatcher.Register("settings.save", p =>
        {
            var settings = p?.GetProperty("settings").Deserialize<AppSettings>(JsonOptions.Create(o => o.WriteIndented = false))
                ?? throw new BridgeException("validation", "无效的设置");
            _store.Config.Settings = settings;
            _store.Save();
            return Task.FromResult<object?>(new SettingsInfo(settings, CommonDirs(), Environment.UserName));
        });

        Dispatcher.Register("workdirs.list", _ => Task.FromResult<object?>(_workdirs.List()));
        Dispatcher.Register("workdirs.add", p =>
        {
            _workdirs.Add(p?.GetProperty("path").GetString() ?? "");
            return Task.FromResult<object?>(_workdirs.List());
        });
        Dispatcher.Register("workdirs.remove", p =>
        {
            _workdirs.Remove(p?.GetProperty("path").GetString() ?? "");
            return Task.FromResult<object?>(_workdirs.List());
        });

        Dispatcher.Register("sessions.list", _ => Task.FromResult<object?>(_terminal.List()));

        Dispatcher.Register("terminal.create", async p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var tool = _store.Config.Tools.FirstOrDefault(t => t.Id == toolId)
                ?? throw new BridgeException("not_found", "工具不存在");
            var profile = ResolveProfile(toolId, p);
            _launch.Validate(tool, profile);
            var command = _launch.BuildCommand(tool, profile);
            var workdir = LaunchService.ResolveWorkdir(profile);
            var (cols, rows) = Size(p);
            var sessionId = await _terminal.CreateAsync(
                tool.Name, command.App, command.Args, workdir, _launch.ResolveEnv(profile), cols, rows);
            RecordUsage(tool, workdir);
            return new { sessionId };
        });

        Dispatcher.Register("terminal.createShell", async p =>
        {
            var (app, title) = _store.Config.Settings.DefaultShell switch
            {
                "pwsh" => (PathSearch.FindOnPath("pwsh")
                           ?? throw new BridgeException("not_found", "未找到 pwsh，请在设置中改用 powershell 或 cmd"), "pwsh"),
                "powershell" => (PathSearch.FindOnPath("powershell") ?? "powershell.exe", "PowerShell"),
                _ => (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "cmd"),
            };
            var (cols, rows) = Size(p);
            var sessionId = await _terminal.CreateAsync(
                title, app!, Array.Empty<string>(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, cols, rows);
            return new { sessionId };
        });

        Dispatcher.Register("terminal.write", p =>
        {
            _terminal.Write(p?.GetProperty("sessionId").GetString() ?? "", p?.GetProperty("data").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.resize", p =>
        {
            _terminal.Resize(p?.GetProperty("sessionId").GetString() ?? "",
                p?.GetProperty("cols").GetInt32() ?? 80, p?.GetProperty("rows").GetInt32() ?? 24);
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.kill", p =>
        {
            _terminal.Kill(p?.GetProperty("sessionId").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.close", p =>
        {
            _terminal.Close(p?.GetProperty("sessionId").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("launch.external", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var tool = _store.Config.Tools.FirstOrDefault(t => t.Id == toolId)
                ?? throw new BridgeException("not_found", "工具不存在");
            var profile = ResolveProfile(toolId, p);
            var pid = _launch.LaunchExternal(tool, profile);
            RecordUsage(tool, LaunchService.ResolveWorkdir(profile));
            return Task.FromResult<object?>(new { pid });
        });
    }

    private List<ToolListItem> ToolsList() =>
        _store.Config.Tools.Select(t => new ToolListItem(
            t,
            File.Exists(t.ExePath),
            _store.Config.Profiles.FirstOrDefault(p => p.ToolId == t.Id)?.OpenMode
                ?? (_store.Config.Settings.PreferEmbedded ? OpenMode.Embedded : OpenMode.External)))
        .ToList();

    private LaunchProfile ResolveProfile(string toolId, JsonElement? p)
    {
        if (p != null && p.Value.TryGetProperty("profileId", out var idEl) &&
            idEl.GetString() is { Length: > 0 } profileId)
        {
            var byId = _store.Config.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (byId != null) return byId;
        }
        return _store.Config.Profiles.FirstOrDefault(x => x.ToolId == toolId) ?? DefaultProfile(toolId);
    }

    private LaunchProfile DefaultProfile(string toolId) => new()
    {
        ToolId = toolId,
        OpenMode = _store.Config.Settings.PreferEmbedded ? OpenMode.Embedded : OpenMode.External,
    };

    private void RecordUsage(ToolInfo tool, string workdir)
    {
        _store.Config.LastUsed = new LastUsedInfo { ToolId = tool.Id, Workdir = workdir };
        _store.Save();
        if (Directory.Exists(workdir)) _workdirs.Add(workdir);
    }

    private static (int cols, int rows) Size(JsonElement? p)
    {
        var cols = p?.TryGetProperty("cols", out var c) == true && c.TryGetInt32(out var cv) ? cv : 120;
        var rows = p?.TryGetProperty("rows", out var r) == true && r.TryGetInt32(out var rv) ? rv : 30;
        return (cols, rows);
    }

    private static List<CommonDir> CommonDirs()
    {
        var dirs = new List<CommonDir>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(string name, string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                dirs.Add(new CommonDir(name, path));
        }
        Add("主目录", home);
        Add("桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("下载", Path.Combine(home, "Downloads"));
        try
        {
            foreach (var drive in Directory.GetLogicalDrives())
                dirs.Add(new CommonDir(drive, drive));
        }
        catch (IOException) { }
        return dirs;
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter BridgeTests` → 预期 11 Passed。
再跑全量：`dotnet test` → 预期全部 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 消息桥——JSON 分发器与全部业务方法"
```

---

## 任务 11：WPF 宿主集成（WebView2 + 桥接线 + 退出确认）

**文件：**
- 修改：`src/ForgeDeck.App/MainWindow.xaml`、`src/ForgeDeck.App/MainWindow.xaml.cs`、`src/ForgeDeck.App/ForgeDeck.App.csproj`

- [ ] **步骤 1：MainWindow.xaml**

替换为：

```xml
<Window x:Class="ForgeDeck.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="ForgeDeck" Width="1280" Height="800"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <wv2:WebView2 x:Name="Web" />
    </Grid>
</Window>
```

- [ ] **步骤 2：MainWindow.xaml.cs**

替换为：

```csharp
using System.ComponentModel;
using System.IO;
using System.Windows;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;
using Microsoft.Web.WebView2;

namespace ForgeDeck.App;

public partial class MainWindow : Window
{
    private readonly ConfigStore _store = new();
    private readonly TerminalSessionManager _terminal = new();
    private readonly ForgeDeckBridge _bridge;
    private bool _confirmedExit;

    public MainWindow()
    {
        InitializeComponent();
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[]
            {
                new KnownDirsScanSource(),
                new PathScanSource(),
                new RegistryScanSource(new RegistryUninstallRegistry()),
                new StartMenuScanSource(new WScriptShellLinkResolver()),
            }),
            _terminal);
        _bridge.Dispatcher.Outgoing += Post;
        Web.DefaultBackgroundColor = System.Windows.Media.Color.FromRgb(14, 18, 17);
        Web.CoreWebView2InitializationCompleted += OnWebReady;
        Closing += OnClosing;
    }

    private void OnWebReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        var core = Web.CoreWebView2;
        core.WebMessageReceived += async (_, args) =>
        {
            var response = await _bridge.Dispatcher.HandleAsync(args.TryGetWebMessageAsString());
            if (response != null) Post(response);
        };
        if (Environment.GetEnvironmentVariable("FORGEDECK_DEV") == "1")
            core.Navigate("http://localhost:5173");
        else
            core.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html")).AbsoluteUri);
    }

    private void Post(string message)
    {
        if (Web.CoreWebView2 == null) return;
        Dispatcher.Invoke(() => Web.CoreWebView2.PostWebMessageAsJson(message));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_confirmedExit || !_terminal.HasRunningSessions || _store.Config.Settings.SkipExitConfirm)
        {
            _terminal.Dispose();
            return;
        }
        e.Cancel = true;
        var running = _terminal.List().Count(s => s.Running);
        var choice = MessageBox.Show(
            $"有 {running} 个会话正在运行，退出将结束它们。确定退出吗？", "ForgeDeck",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Yes)
        {
            _confirmedExit = true;
            Close();
        }
    }
}
```

- [ ] **步骤 3：csproj 引入前端产物**

`src/ForgeDeck.App/ForgeDeck.App.csproj` 的 `<ItemGroup>` 区新增（dist 缺失时不影响构建）：

```xml
<ItemGroup>
  <Content Include="..\..\ui\dist\**\*.*" CopyToOutputDirectory="PreserveNewest">
    <Link>wwwroot\%(RecursiveDir)%(Filename)%(Extension)</Link>
  </Content>
</ItemGroup>
```

- [ ] **步骤 4：构建与联调验证**

运行：`dotnet build` → 预期 0 错误。
再运行：`cd ui && npm run build`（生成 dist），然后仓库根 `FORGEDECK_DEV=1 dotnet run --project src/ForgeDeck.App`（另开终端 `cd ui && npm run dev`）。
预期：窗口打开显示应用壳；在 WebView2 里按 F12 打开 DevTools，Console 执行 `window.chrome.webview.postMessage(JSON.stringify({id:1,method:'app.info'}))` 能收到响应（Console 里可见返回消息事件）。
关掉 dev 服务器后直接 `dotnet run --project src/ForgeDeck.App`（无 FORGEDECK_DEV）→ 加载 wwwroot/index.html 显示同一壳。

- [ ] **步骤 5：Commit**

```bash
git add src/ForgeDeck.App
git commit -m "feat(app): WebView2 宿主——消息桥接线/开发模式导航/退出确认/前端产物打包"
```

---

## 任务 12：内嵌终端面板（xterm.js）

**文件：**
- 创建：`ui/src/TerminalPanel.tsx`
- 修改：`ui/src/App.tsx`

- [ ] **步骤 1：TerminalPanel 组件**

`ui/src/TerminalPanel.tsx`：

```tsx
import { useEffect, useRef } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { bridge } from './bridge';
import type { TerminalSessionInfo } from './types';

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
  const terms = useRef(new Map<string, { term: Terminal; fit: FitAddon; container: HTMLDivElement }>());
  const containers = useRef(new Map<string, HTMLDivElement>());
  const observers = useRef(new Map<string, ResizeObserver>());

  useEffect(() => bridge.on('terminal.data', ({ sessionId, chunk }: any) => {
    terms.current.get(sessionId)?.term.write(chunk);
  }), []);

  useEffect(() => {
    for (const [id, entry] of terms.current)
      if (!sessions.some((s) => s.sessionId === id)) {
        entry.term.dispose();
        observers.current.get(id)?.disconnect();
        observers.current.delete(id);
        terms.current.delete(id);
        containers.current.delete(id);
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
      term.onData((data) => bridge.request('terminal.write', { sessionId: id, data }).catch(() => {}));
      const observer = new ResizeObserver(() => {
        if (container.offsetParent === null) return;
        try { fit.fit(); } catch { return; }
        bridge.request('terminal.resize', { sessionId: id, cols: term.cols, rows: term.rows }).catch(() => {});
      });
      observer.observe(container);
      terms.current.set(id, { term, fit, container });
      observers.current.set(id, observer);
    }
  }, [sessions]);

  useEffect(() => {
    const entry = activeId ? terms.current.get(activeId) : null;
    if (entry && entry.container.offsetParent !== null)
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
```

- [ ] **步骤 2：App 接入会话状态**

`ui/src/App.tsx` 中（在任务 3 的壳基础上增量修改）：

新增 import 与状态：

```tsx
import { useCallback, useEffect, useState } from 'react';
import { bridge } from './bridge';
import { TerminalPanel } from './TerminalPanel';
import type { TerminalSessionInfo } from './types';
```

组件体内：

```tsx
const [sessions, setSessions] = useState<TerminalSessionInfo[]>([]);
const [activeSessionId, setActiveSessionId] = useState<string | null>(null);

const refreshSessions = useCallback(async () => {
  setSessions(await bridge.request<TerminalSessionInfo[]>('sessions.list'));
}, []);

useEffect(() => bridge.on('sessions.changed', () => { refreshSessions(); }), [refreshSessions]);

useEffect(() => {
  if (activeSessionId == null && sessions.length > 0) setActiveSessionId(sessions[0].sessionId);
}, [sessions, activeSessionId]);

const handleNewShell = useCallback(async () => {
  try {
    const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.createShell', { cols: 120, rows: 30 });
    setActiveSessionId(sessionId);
    await refreshSessions();
  } catch (e: any) { console.error(e); } // Toast 在任务 16 接入
}, [refreshSessions]);

const handleCloseSession = useCallback(async (id: string) => {
  await bridge.request('terminal.close', { sessionId: id }).catch(() => {});
  await refreshSessions();
}, [refreshSessions]);
```

把任务 3 的终端占位 `<section className="terminal">…</section>` 替换为：

```tsx
<TerminalPanel sessions={sessions} activeId={activeSessionId}
  onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
```

并在启动 effect 里加 `refreshSessions()`（任务 13 会统一整理启动加载）。

- [ ] **步骤 3：构建与真机联调验证**

运行：`cd ui && npm run build` → `✓ built`。
联调（`npm run dev` + `FORGEDECK_DEV=1 dotnet run --project src/ForgeDeck.App`）：
1. 点终端区 `+` 新建标签 → 出现 pwsh/cmd 提示符（真实 ConPTY）。
2. 敲 `echo forge-e2e` 回车 → 输出回显。
3. 拖动窗口宽度 → 终端随宽重排（FitAddon + resize）。
4. 点标签 `×` → 标签与实例销毁。
5. 关闭应用窗口 → 若有运行中会话弹确认框。

- [ ] **步骤 4：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 内嵌终端面板——xterm 多会话/自适应尺寸/输入输出流"
```

---

## 任务 13：快速启动页（指标 + 工具列表 + 手动添加）

**文件：**
- 创建：`ui/src/LauncherView.tsx`、`ui/src/ToolListPanel.tsx`、`ui/src/Modal.tsx`、`ui/src/AddToolModal.tsx`、`ui/src/lib/format.ts`
- 修改：`ui/src/App.tsx`（本任务引入数据加载主线，替换 launcher 占位）

- [ ] **步骤 1：工具函数**

`ui/src/lib/format.ts`：

```ts
export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '从未';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '从未';
  const diff = Date.now() - then;
  if (diff < 60_000) return '刚刚';
  if (diff < 3_600_000) return `${Math.floor(diff / 60_000)} 分钟前`;
  if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)} 小时前`;
  return new Date(iso).toLocaleDateString('zh-CN');
}

export function baseName(path: string): string {
  const parts = path.replace(/[\\/]+$/, '').split(/[\\/]/);
  return parts[parts.length - 1] || path;
}
```

- [ ] **步骤 2：Modal 基座（含 Esc/关闭动画）**

`ui/src/Modal.tsx`：

```tsx
import { useEffect, useState, type ReactNode } from 'react';

export function Modal({ open, onClose, title, subtitle, wide, children }: {
  open: boolean; onClose: () => void; title: string; subtitle?: string; wide?: boolean; children: ReactNode;
}) {
  const [closing, setClosing] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') requestClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  });

  const requestClose = () => {
    if (!open || closing) return;
    setClosing(true);
    setTimeout(() => { setClosing(false); onClose(); }, 140);
  };

  if (!open && !closing) return null;
  return (
    <div className={`modal-wrap${open ? ' show' : ''}${closing ? ' closing' : ''}`}
      role="dialog" aria-modal="true" onClick={(e) => { if (e.target === e.currentTarget) requestClose(); }}>
      <div className={`modal${wide ? ' picker-modal' : ''}`}>
        <div className="modal-head">
          <div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>
          <button className="icon-btn" aria-label="关闭" onClick={requestClose}>×</button>
        </div>
        {children}
      </div>
    </div>
  );
}
```

- [ ] **步骤 3：AddToolModal**

`ui/src/AddToolModal.tsx`：

```tsx
import { useState } from 'react';
import { Modal } from './Modal';

export function AddToolModal({ open, onClose, onConfirm }: {
  open: boolean; onClose: () => void;
  onConfirm: (name: string, exePath: string) => Promise<void>;
}) {
  const [name, setName] = useState('');
  const [path, setPath] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!name.trim() || !path.trim()) { setError('请填写工具名称与可执行文件路径'); return; }
    try {
      await onConfirm(name.trim(), path.trim());
      setName(''); setPath(''); setError(null);
    } catch (e: any) { setError(e.message); }
  };

  return (
    <Modal open={open} onClose={onClose} title="添加本地工具" subtitle="将未被自动识别的 CLI 工具加入启动列表。">
      <div className="field">
        <label htmlFor="newName">工具名称</label>
        <input className="input" id="newName" placeholder="例如：Gemini CLI" value={name}
          onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="field">
        <label htmlFor="newPath">可执行文件路径</label>
        <input className="input mono" id="newPath" placeholder="C:\\Program Files\\...\\tool.exe" value={path}
          onChange={(e) => setPath(e.target.value)} />
      </div>
      {error && <p style={{ color: '#e5484d', fontSize: 12, margin: '0 0 8px' }}>{error}</p>}
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>取消</button>
        <button className="btn primary" onClick={submit}>添加到工具库</button>
      </div>
    </Modal>
  );
}
```

- [ ] **步骤 4：ToolListPanel**

`ui/src/ToolListPanel.tsx`：

```tsx
import type { ToolListItem } from './types';

const LOGOS: Record<string, string> = {
  'Claude Code': 'C/', 'Codex CLI': 'CX', 'Gemini CLI': 'G', 'Aider': 'Ai',
  'Cursor': 'Cu', 'Cursor Agent': 'Cu', 'Windsurf': 'W', 'Trae': 'T', 'Zed': 'Z', 'VS Code': 'VS',
};
const logoFor = (name: string) => LOGOS[name] ?? name.slice(0, 2);

export function ToolListPanel({ tools, scanning, selectedToolId, onSelect, onRescan }: {
  tools: ToolListItem[]; scanning: boolean; selectedToolId: string | null;
  onSelect: (id: string) => void; onRescan: () => void;
}) {
  return (
    <section className="panel">
      <div className="panel-head">
        <span className="panel-title">本机工具</span>
        <span className="panel-meta">{scanning ? '正在扫描…' : '自动扫描 · 已完成'}</span>
      </div>
      <div className="tool-list">
        {tools.map((item) => (
          <div key={item.tool.id}
            className={`tool${item.tool.id === selectedToolId ? ' selected' : ''}`}
            role="button" tabIndex={0}
            onClick={() => onSelect(item.tool.id)}
            onKeyDown={(e) => { if (e.key === 'Enter') onSelect(item.tool.id); }}>
            <div className="tool-logo">{logoFor(item.tool.name)}</div>
            <div>
              <div className="tool-name">{item.tool.name}</div>
              <div className="tool-path" title={item.tool.exePath}>{item.tool.exePath}</div>
            </div>
            <div>
              <div className="tool-status">{item.exists ? '已安装' : '文件缺失'}</div>
              <button className="tool-menu" aria-label={`打开 ${item.tool.name} 配置`}
                onClick={(e) => { e.stopPropagation(); onSelect(item.tool.id); }}>···</button>
            </div>
          </div>
        ))}
        {tools.length === 0 && !scanning &&
          <div className="tool-path" style={{ padding: '14px 10px' }}>未识别到已安装工具，试试重新扫描或手动添加。</div>}
      </div>
      <div className="scan-row">
        <span>{scanning ? '正在检查 PATH 与已知安装位置' : '已扫描已知目录、PATH、注册表、开始菜单'}</span>
        <button className="btn small" onClick={onRescan} disabled={scanning}>重新扫描</button>
      </div>
    </section>
  );
}
```

- [ ] **步骤 5：LauncherView（指标区 + 双栏骨架）**

`ui/src/LauncherView.tsx`（右侧配置面板任务 14 再放，本任务先渲染占位面板保持布局完整）：

```tsx
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
```

- [ ] **步骤 6：App 接入数据主线**

`ui/src/App.tsx` 更新（关键增量；与任务 12 的会话代码合并后的完整形态）：

```tsx
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
  const [workdirs, setWorkdirs] = useState<string[]>([]);
  const [selectedToolId, setSelectedToolId] = useState<string | null>(null);
  const [profile, setProfile] = useState<LaunchProfile | null>(null);
  const [scanning, setScanning] = useState(false);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  const refreshSessions = useCallback(async () => {
    setSessions(await bridge.request<TerminalSessionInfo[]>('sessions.list'));
  }, []);
  const refreshWorkdirs = useCallback(async () => {
    setWorkdirs(await bridge.request<string[]>('workdirs.list'));
  }, []);

  const selectTool = useCallback(async (toolId: string) => {
    setSelectedToolId(toolId);
    setProfile(await bridge.request<LaunchProfile>('profiles.get', { toolId }));
  }, []);

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
          setAppInfo(await bridge.request<AppInfo>('app.info'));
        } finally { setScanning(false); }
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

  useEffect(() => {
    if (activeSessionId == null && sessions.length > 0) setActiveSessionId(sessions[0].sessionId);
  }, [sessions, activeSessionId]);

  const handleRescan = useCallback(async () => {
    setScanning(true);
    try {
      setTools(await bridge.request<ToolListItem[]>('tools.rescan'));
      setAppInfo(await bridge.request<AppInfo>('app.info'));
    } finally { setScanning(false); }
  }, []);

  const handleAddTool = useCallback(async (name: string, exePath: string) => {
    const list = await bridge.request<ToolListItem[]>('tools.addManual', { name, exePath });
    setTools(list);
    setAddOpen(false);
  }, []);

  const handleNewShell = useCallback(async () => {
    const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.createShell', { cols: 120, rows: 30 });
    setActiveSessionId(sessionId);
    await refreshSessions();
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
```

- [ ] **步骤 7：构建与联调验证**

运行：`cd ui && npm run build` → `✓ built`。
联调验证：启动后自动扫描（真实机器应识别出本机已装的已知工具）；三个指标卡显示真实数据；点"手动添加工具"填一个不存在的路径 → 弹窗内显示错误；填 `C:\Windows\System32\cmd.exe` 名称 `Cmd 手动` → 列表新增；顶栏刷新按钮触发重扫。

- [ ] **步骤 8：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 快速启动页——指标/工具列表/扫描/手动添加"
```

---

## 任务 14：启动配置面板与工作目录控件

**文件：**
- 创建：`ui/src/ConfigPanel.tsx`、`ui/src/WorkdirControl.tsx`、`ui/src/FolderPickerModal.tsx`、`ui/src/Switch.tsx`、`ui/src/lib/env.ts`
- 修改：`ui/src/App.tsx`（替换占位 configPanel、接启动/保存流程）

- [ ] **步骤 1：env 文本解析**

`ui/src/lib/env.ts`：

```ts
export function parseEnvText(text: string): Record<string, string> {
  const env: Record<string, string> = {};
  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;
    const eq = trimmed.indexOf('=');
    if (eq <= 0) continue;
    env[trimmed.slice(0, eq).trim()] = trimmed.slice(eq + 1);
  }
  return env;
}

export function stringifyEnv(env: Record<string, string>): string {
  return Object.entries(env).map(([k, v]) => `${k}=${v}`).join('\n');
}
```

- [ ] **步骤 2：Switch 与 WorkdirControl**

`ui/src/Switch.tsx`：

```tsx
export function Switch({ on, label, onToggle }: { on: boolean; label: string; onToggle: () => void }) {
  return (
    <div className="switch-row">
      <span>{label}</span>
      <button className={`switch${on ? ' on' : ''}`} aria-label={label} onClick={onToggle}><i /></button>
    </div>
  );
}
```

`ui/src/WorkdirControl.tsx`：

```tsx
import { useEffect, useRef, useState } from 'react';

export function WorkdirControl({ value, recent, onChange, onBrowse }: {
  value: string; recent: string[]; onChange: (v: string) => void; onBrowse: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const onClick = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setMenuOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false); };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('click', onClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [menuOpen]);

  return (
    <div className="workdir-control" ref={ref}>
      <input className="input mono" id="workdir" value={value} onChange={(e) => onChange(e.target.value)} />
      <button className="workdir-btn" type="button" aria-label="打开最近工作目录"
        aria-expanded={menuOpen} aria-controls="workdirMenu"
        onClick={() => setMenuOpen((v) => !v)}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="m6 9 6 6 6-6" /></svg>
      </button>
      <button className="workdir-btn" type="button" aria-label="选择工作文件夹" onClick={onBrowse}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M3 7.5h7l2 2h9v9H3z" /><path d="M3 7.5V5h7l2 2.5" /></svg>
      </button>
      {menuOpen && (
        <div className="workdir-menu show" id="workdirMenu" role="menu">
          <div className="workdir-menu-title">最近与常用目录</div>
          {recent.length === 0 && <div className="workdir-option" style={{ color: 'var(--muted)', cursor: 'default' }}>暂无历史记录</div>}
          {recent.slice(0, 5).map((p) => (
            <button key={p} className="workdir-option" type="button" role="menuitem"
              onClick={() => { onChange(p); setMenuOpen(false); }}>{p}</button>
          ))}
        </div>
      )}
    </div>
  );
}
```

- [ ] **步骤 3：FolderPickerModal**

`ui/src/FolderPickerModal.tsx`：

```tsx
import { useEffect, useMemo, useState } from 'react';
import { Modal } from './Modal';
import { baseName } from './lib/format';
import type { CommonDir } from './types';

export function FolderPickerModal({ open, initialValue, commonDirs, workdirs, onConfirm, onClose }: {
  open: boolean; initialValue: string; commonDirs: CommonDir[]; workdirs: string[];
  onConfirm: (path: string) => void; onClose: () => void;
}) {
  const [path, setPath] = useState(initialValue);
  const [active, setActive] = useState(initialValue);

  useEffect(() => { if (open) { setPath(initialValue); setActive(initialValue); } }, [open, initialValue]);

  const entries = useMemo(() => {
    const seen = new Set<string>();
    const list: { name: string; path: string }[] = [];
    for (const dir of [...workdirs, ...commonDirs.map((d) => d.path)]) {
      const key = dir.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      const named = commonDirs.find((d) => d.path === dir);
      list.push({ name: named?.name ?? baseName(dir), path: dir });
    }
    return list.slice(0, 12);
  }, [workdirs, commonDirs]);

  return (
    <Modal open={open} onClose={onClose} wide title="选择工作文件夹" subtitle="选择后将以完整 Windows 路径写入启动配置。">
      <div className="picker-path">
        <input className="input mono" aria-label="文件夹路径" value={path} onChange={(e) => setPath(e.target.value)} />
      </div>
      <div className="section-label">常用位置</div>
      <div className="picker-list">
        {entries.map((entry) => (
          <button key={entry.path} type="button" data-path={entry.path}
            className={`picker-folder${entry.path === active ? ' active' : ''}`}
            onClick={() => { setActive(entry.path); setPath(entry.path); }}>
            {entry.name}<small>{entry.path}</small>
          </button>
        ))}
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>取消</button>
        <button className="btn primary" onClick={() => path.trim() && onConfirm(path.trim())}>选择此文件夹</button>
      </div>
    </Modal>
  );
}
```

- [ ] **步骤 4：ConfigPanel**

`ui/src/ConfigPanel.tsx`：

```tsx
import { useEffect, useState } from 'react';
import { Switch } from './Switch';
import { WorkdirControl } from './WorkdirControl';
import { parseEnvText, stringifyEnv } from './lib/env';
import type { LaunchProfile, OpenMode, ToolListItem } from './types';

// 与后端 KnownTools.ResumeArgs 对应：目前仅 Claude Code 有 --continue
const RESUMABLE = new Set(['Claude Code']);

export function ConfigPanel({ tool, profile, workdirs, onSave, onLaunch, onBrowse }: {
  tool: ToolListItem; profile: LaunchProfile; workdirs: string[];
  onSave: (p: LaunchProfile) => void; onLaunch: (p: LaunchProfile) => void;
  onBrowse: () => void;
}) {
  const [args, setArgs] = useState(profile.args);
  const [workdir, setWorkdir] = useState(profile.workdir);
  const [envText, setEnvText] = useState(stringifyEnv(profile.env));
  const [autoRestore, setAutoRestore] = useState(profile.autoRestore);
  const [openMode, setOpenMode] = useState<OpenMode>(profile.openMode);
  const [savedFlash, setSavedFlash] = useState(false);

  useEffect(() => {
    setArgs(profile.args);
    setEnvText(stringifyEnv(profile.env));
    setAutoRestore(profile.autoRestore);
    setOpenMode(profile.openMode);
  }, [profile.id]);

  // 工作目录单独跟随：文件夹选择弹窗直接更新 App 层 profile，需要立即回显
  useEffect(() => setWorkdir(profile.workdir), [profile.workdir]);

  const current = (): LaunchProfile => ({
    ...profile, args, workdir, env: parseEnvText(envText), autoRestore, openMode,
  });
  const save = () => {
    onSave(current());
    setSavedFlash(true);
    setTimeout(() => setSavedFlash(false), 1400);
  };

  return (
    <section className="panel">
      <div className="panel-head">
        <span className="panel-title">启动配置</span>
        <span className="panel-meta">{savedFlash ? '已保存' : '未保存更改'}</span>
      </div>
      <div className="config">
        <div className="config-top">
          <div className="tool-logo">{tool.tool.name.slice(0, 2)}</div>
          <div>
            <h2>{tool.tool.name}</h2>
            <p>{tool.tool.exePath.split('\\').pop()}</p>
          </div>
        </div>
        <div className="config-section">
          <div className="section-label">启动参数</div>
          <div className="field">
            <label htmlFor="args">参数</label>
            <input className="input mono" id="args" value={args} onChange={(e) => setArgs(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="workdir">工作目录</label>
            <WorkdirControl value={workdir} recent={workdirs}
              onChange={setWorkdir} onBrowse={onBrowse} />
          </div>
        </div>
        <div className="config-section">
          <div className="section-label">环境变量</div>
          <div className="field">
            <label htmlFor="env">每行一个 KEY=VALUE</label>
            <textarea className="textarea" id="env" value={envText} onChange={(e) => setEnvText(e.target.value)} />
          </div>
          {RESUMABLE.has(tool.tool.name) && tool.tool.builtin && (
            <Switch on={autoRestore} label="启动时自动恢复上次会话" onToggle={() => setAutoRestore((v) => !v)} />
          )}
        </div>
        <div className="config-section">
          <div className="section-label">运行方式</div>
          <div className="choice-row" id="launchMode">
            <button className={`choice${openMode === 'embedded' ? ' active' : ''}`} onClick={() => setOpenMode('embedded')}>
              <strong>内嵌终端</strong><br /><span>在下方新建会话标签</span>
            </button>
            <button className={`choice${openMode === 'external' ? ' active' : ''}`} onClick={() => setOpenMode('external')}>
              <strong>独立窗口</strong><br /><span>在新窗口中打开</span>
            </button>
          </div>
        </div>
        <div className="config-actions">
          <button className="btn primary" onClick={() => onLaunch(current())}>
            <svg viewBox="0 0 24 24" fill="currentColor"><path d="m8 5 11 7-11 7V5Z" /></svg>
            启动工具
          </button>
          <button className="btn" onClick={save}>{savedFlash ? '已保存' : '保存配置'}</button>
        </div>
      </div>
    </section>
  );
}
```

- [ ] **步骤 5：App 接入配置面板与启动流程**

`ui/src/App.tsx`：新增 import（`ConfigPanel`、`FolderPickerModal`）、状态 `const [pickerOpen, setPickerOpen] = useState(false);`，替换任务 13 的 configPanel 占位：

```tsx
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
)}
```

新增处理函数：

```tsx
const handleSaveProfile = useCallback(async (p: LaunchProfile) => {
  setProfile(await bridge.request<LaunchProfile>('profiles.save', { profile: p }));
}, []);

const handleLaunch = useCallback(async (p: LaunchProfile) => {
  const tool = tools.find((t) => t.tool.id === p.toolId);
  if (!tool) return;
  try {
    await bridge.request('profiles.save', { profile: p }); // 启动即保存当前配置
    if (p.openMode === 'embedded') {
      const { sessionId } = await bridge.request<{ sessionId: string }>('terminal.create',
        { toolId: p.toolId, profileId: p.id, cols: 120, rows: 30 });
      setActiveSessionId(sessionId);
      await refreshSessions();
    } else {
      await bridge.request('launch.external', { toolId: p.toolId, profileId: p.id });
    }
    setAppInfo(await bridge.request<AppInfo>('app.info'));
    await refreshWorkdirs();
  } catch (e: any) { console.error('启动失败', e); } // Toast 在任务 16 接入
}, [tools, refreshSessions, refreshWorkdirs]);
```

在 `<AddToolModal>` 之后渲染：

```tsx
<FolderPickerModal
  open={pickerOpen}
  initialValue={profile?.workdir || ''}
  commonDirs={settingsInfo?.commonDirs ?? []}
  workdirs={workdirs}
  onConfirm={(path) => { setProfile((p) => (p ? { ...p, workdir: path } : p)); setPickerOpen(false); }}
  onClose={() => setPickerOpen(false)} />
```


- [ ] **步骤 6：构建与联调验证**

运行：`cd ui && npm run build` → `✓ built`。
联调验证：
1. 选中 `Cmd 手动`（或任一已装工具），工作目录点文件夹按钮 → 弹窗列出真实常用目录；选择后回填输入框。
2. 下拉按钮 → 最近 5 条历史。
3. 参数填 `/k`、运行方式=内嵌终端 → 点"启动工具" → 底部新标签出现 `cmd` 交互终端（`/k` 保持存活）。
4. 运行方式=独立窗口 → 点启动 → 弹出独立 cmd 窗口，工作目录正确。
5. Claude Code（若已装）显示"自动恢复会话"开关；保存后重启应用配置仍在。

- [ ] **步骤 7：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 启动配置面板——参数/工作目录控件/环境变量/运行方式/启动流程"
```

---

## 任务 15：工具库、终端会话与设置视图

**文件：**
- 创建：`ui/src/ToolsView.tsx`、`ui/src/SessionsView.tsx`、`ui/src/SettingsView.tsx`
- 修改：`ui/src/App.tsx`（替换三个占位视图）

- [ ] **步骤 1：ToolsView**

`ui/src/ToolsView.tsx`：

```tsx
import type { ToolListItem } from './types';

export function ToolsView({ tools, onRescan }: { tools: ToolListItem[]; onRescan: () => void }) {
  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">TOOL REGISTRY / 02</p>
          <h1 className="title">工具库</h1>
          <p className="sub">集中查看本机识别结果、安装位置和默认启动方式。</p>
        </div>
        <button className="btn" onClick={onRescan}>扫描本机工具</button>
      </div>
      <div className="data-panel">
        <table className="data-table">
          <thead>
            <tr><th>工具</th><th>可执行文件</th><th>来源</th><th>默认方式</th><th>状态</th></tr>
          </thead>
          <tbody>
            {tools.map((item) => (
              <tr key={item.tool.id}>
                <td><strong>{item.tool.name}</strong></td>
                <td className="path-cell">{item.tool.exePath}</td>
                <td>{item.tool.source}</td>
                <td>{item.defaultMode === 'embedded' ? '内嵌终端' : '独立窗口'}</td>
                <td className="status-text">{item.exists ? '已安装' : '文件缺失'}</td>
              </tr>
            ))}
            {tools.length === 0 && <tr><td colSpan={5} className="path-cell">尚未识别到工具。</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  );
}
```

- [ ] **步骤 2：SessionsView**

`ui/src/SessionsView.tsx`：

```tsx
import type { TerminalSessionInfo } from './types';

export function SessionsView({ sessions, onNewShell }: {
  sessions: TerminalSessionInfo[]; onNewShell: () => void;
}) {
  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">TERMINAL SESSIONS / 03</p>
          <h1 className="title">终端会话</h1>
          <p className="sub">管理正在运行的工具、工作目录和会话状态。</p>
        </div>
        <button className="btn" onClick={onNewShell}>新建空白会话</button>
      </div>
      <div className="session-grid">
        {sessions.length === 0 &&
          <p className="sub" style={{ gridColumn: '1 / -1' }}>暂无会话。启动工具或新建空白会话后在此查看。</p>}
        {sessions.map((s) => (
          <article key={s.sessionId} className="session-card">
            <div>
              <h2>{s.title}</h2>
              <p className="mono">{s.workdir}</p>
            </div>
            <span className="status-text">
              {s.running ? '运行中' : `已退出${s.exitCode != null ? ` · ${s.exitCode}` : ''}`}
            </span>
          </article>
        ))}
      </div>
    </>
  );
}
```

- [ ] **步骤 3：SettingsView**

`ui/src/SettingsView.tsx`：

```tsx
import { useEffect, useState } from 'react';
import { Switch } from './Switch';
import type { AppSettings, SettingsInfo } from './types';

export function SettingsView({ info, onSave }: { info: SettingsInfo; onSave: (s: AppSettings) => void }) {
  const [extraDirs, setExtraDirs] = useState(info.settings.extraScanDirs.join('\n'));
  const [autoScan, setAutoScan] = useState(info.settings.autoScanOnStartup);
  const [shell, setShell] = useState(info.settings.defaultShell);
  const [skipExitConfirm, setSkipExitConfirm] = useState(info.settings.skipExitConfirm);
  const [preferEmbedded, setPreferEmbedded] = useState(info.settings.preferEmbedded);

  useEffect(() => {
    setExtraDirs(info.settings.extraScanDirs.join('\n'));
    setAutoScan(info.settings.autoScanOnStartup);
    setShell(info.settings.defaultShell);
    setSkipExitConfirm(info.settings.skipExitConfirm);
    setPreferEmbedded(info.settings.preferEmbedded);
  }, [info]);

  const save = () => onSave({
    ...info.settings,
    defaultShell: (shell.trim() || 'pwsh') as AppSettings['defaultShell'],
    autoScanOnStartup: autoScan,
    extraScanDirs: extraDirs.split(/\r?\n/).map((s) => s.trim()).filter(Boolean),
    skipExitConfirm,
    preferEmbedded,
  });

  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">SYSTEM PREFERENCES / 04</p>
          <h1 className="title">设置</h1>
          <p className="sub">调整扫描范围、终端行为和启动器偏好。</p>
        </div>
      </div>
      <div className="settings-grid">
        <article className="setting-card">
          <h2>工具发现</h2>
          <p>控制启动器自动检查的本机位置。</p>
          <div className="field">
            <label htmlFor="scanPaths">附加扫描目录</label>
            <textarea className="textarea" id="scanPaths" value={extraDirs}
              onChange={(e) => setExtraDirs(e.target.value)} />
          </div>
          <Switch on={autoScan} label="启动时自动扫描" onToggle={() => setAutoScan((v) => !v)} />
        </article>
        <article className="setting-card">
          <h2>终端偏好</h2>
          <p>设置新会话的默认 Shell 与运行方式。</p>
          <div className="field">
            <label htmlFor="defaultShell">默认 Shell（pwsh / powershell / cmd）</label>
            <input className="input mono" id="defaultShell" value={shell}
              onChange={(e) => setShell(e.target.value)} />
          </div>
          <Switch on={skipExitConfirm} label="关闭应用时不弹会话确认" onToggle={() => setSkipExitConfirm((v) => !v)} />
          <Switch on={preferEmbedded} label="优先使用内嵌终端" onToggle={() => setPreferEmbedded((v) => !v)} />
        </article>
      </div>
      <div className="setting-actions">
        <button className="btn primary" onClick={save}>保存设置</button>
      </div>
    </>
  );
}
```

（设计稿文案"关闭应用时保留会话"按规格修正为其实际语义"关闭应用时不弹会话确认"，其余文案与设计稿一致。）

- [ ] **步骤 4：App 替换占位视图**

`ui/src/App.tsx` 中三个占位 section 替换为：

```tsx
<section className="view-panel" data-view-panel="tools" hidden={view !== 'tools'}>
  <ToolsView tools={tools} onRescan={() => { setView('launcher'); handleRescan(); }} />
</section>
<section className="view-panel" data-view-panel="sessions" hidden={view !== 'sessions'}>
  <SessionsView sessions={sessions} onNewShell={handleNewShell} />
</section>
<section className="view-panel" data-view-panel="settings" hidden={view !== 'settings'}>
  {settingsInfo && <SettingsView info={settingsInfo} onSave={handleSaveSettings} />}
</section>
```

新增保存函数：

```tsx
const handleSaveSettings = useCallback(async (settings: AppSettings) => {
  setSettingsInfo(await bridge.request<SettingsInfo>('settings.save', { settings }));
}, []);
```

（补 import：`ToolsView`、`SessionsView`、`SettingsView`、类型 `AppSettings`。）

- [ ] **步骤 5：构建与联调验证**

运行：`cd ui && npm run build` → `✓ built`。
联调验证：工具库表格数据/来源标签正确；会话页卡片随内嵌启动实时出现"运行中"，kill 后变"已退出"；设置页改默认 Shell 为 `cmd` 保存 → 新建空白会话变 cmd；附加扫描目录填一个含 `claude.cmd` 的目录 → 重新扫描后出现该工具（来源=附加目录）；开关"关闭应用时不弹会话确认"后退出不再弹确认。

- [ ] **步骤 6：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 工具库/终端会话/设置视图接入真实数据"
```

---

## 任务 16：错误 Toast、体验收尾与验收

**文件：**
- 创建：`ui/src/Toast.tsx`
- 修改：`ui/src/App.tsx`（toast 接线：启动失败、添加成功等）、`README.md`

- [ ] **步骤 1：Toast 组件**

`ui/src/Toast.tsx`：

```tsx
export interface ToastItem { id: number; text: string; kind: 'info' | 'error' }

export function Toast({ items }: { items: ToastItem[] }) {
  if (items.length === 0) return null;
  return (
    <div className="toast-wrap">
      {items.map((t) => (
        <div key={t.id} className={`toast${t.kind === 'error' ? ' error' : ''}`}>{t.text}</div>
      ))}
    </div>
  );
}
```

- [ ] **步骤 2：App 接线**

`ui/src/App.tsx`：

```tsx
const [toasts, setToasts] = useState<ToastItem[]>([]);
const toast = useCallback((text: string, kind: ToastItem['kind'] = 'info') => {
  const item: ToastItem = { id: Date.now() + Math.random(), text, kind };
  setToasts((prev) => [...prev, item]);
  setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== item.id)), 3200);
}, []);
```

把任务 13/14 中 `console.error` 的失败分支替换为 `toast(e.message, 'error')`（`handleLaunch`、`handleNewShell`、启动加载 catch、`handleAddTool` 在 AddToolModal 内已就地展示错误，无需 toast）；`handleSaveProfile`/`handleSaveSettings` 成功后 `toast('已保存')`；`handleAddTool` 成功后 `toast('已添加到工具库')`；独立窗口启动成功 `toast(\`已在独立窗口打开 ${tool.tool.name}\`)`。渲染 `<Toast items={toasts} />` 于根 div 末尾。

- [ ] **步骤 3：验收清单（对照规格与设计稿逐项跑）**

运行全量测试：`dotnet test` → 预期全部 Passed。
构建发布产物：`cd ui && npm run build`，然后仓库根 `dotnet build`。
以**非 dev 模式**启动 `dotnet run --project src/ForgeDeck.App` 逐项验证：

1. 快速启动：自动扫描、三指标、工具列表与配置面板联动、手动添加（成功/失败路径）。
2. 工作目录：历史下拉（最近 5）、文件夹选择弹窗（常用位置=真实目录+历史）、手输路径。
3. 内嵌终端：启动工具进新标签、输入输出、resize、关标签、新建空白会话（默认 Shell 遵循设置）。
4. 独立窗口：参数/环境变量/工作目录生效（用 `cmd /k set FOO=bar` 类命令验证 env）。
5. 环境变量：`KEY=VALUE` 多行保存，启动的进程内 `set KEY` 可见。
6. 自动恢复开关：Claude Code 开启后启动命令追加 `--continue`（终端回显可见）。
7. 工具库/会话/设置三视图数据与行为。
8. 退出确认：有运行会话时弹确认；设置开关后不弹。
9. 视觉对照：并排打开 `docs/design/Web-Prototype/ai-tool-launcher.html` 与应用（1280×800），逐视图核对布局/配色/间距/交互动效（视图切换动画、弹窗进出场、focus-visible、开关动效）。
10. 响应式：窗口缩到 920px 以下侧栏收窄为图标；560px 以下指标单列（WebView2 拖拽窗口宽度验证）。

发现问题回改对应任务组件，全部通过后进入下一步。

- [ ] **步骤 4：更新 README 并 Commit**

`README.md` 增加一节「功能现状」简述 MVP 已支持能力（扫描/配置/两种启动/工作目录历史/设置），保持简洁。

```bash
git add ui/src README.md
git commit -m "feat(ui): 错误 Toast 与验收收尾"
```

---

## 计划自检记录

- **规格覆盖度**：§2 目标 1→任务 6/7（扫描）+13（手动添加）；目标 2→任务 14（配置面板）；目标 3→任务 9/12（内嵌终端）；目标 4→任务 8/14（独立窗口）；目标 5→任务 5/14（工作目录历史）；目标 6→任务 15（设置页）。§3 桥接方法→任务 10 全量注册（`dialog.selectDirectory` 已按规格更新移除，改应用内选择器）。§4.1 数据源→任务 6/7；§4.2 配置→任务 4/5；§4.3 启动包装→任务 8；§4.4 终端→任务 9；§4.5 桥→任务 10/11。§5 前端四视图+弹窗+Mock→任务 1/2/3/12/13/14/15。§6 流程→任务 13/14 启动主线；§7 错误→任务 10（错误封包）/11（退出确认）/16（Toast）+任务 4（损坏恢复）；§8 测试→各任务 TDD 步骤。无遗漏。
- **占位符扫描**：无"待定/TODO"；任务 13 的 configPanel 占位是任务内的显式中间态，任务 14 替换为真实现，链路闭合。
- **类型一致性**：`LaunchProfile.autoRestore`（任务 4 定义，8/10/14 使用）；`ScanHit(ExePath, Known, SourceLabel)`（任务 6 定义，7 使用）；`TerminalSessionInfo(SessionId, Title, Workdir, Running, ExitCode)`（任务 9 定义，10/12/15 使用）；前端 `ToolListItem{tool,exists,defaultMode}`（任务 2 定义，与任务 10 C# `ToolListItem(Tool, Exists, DefaultMode)` 序列化一致）；桥方法名前后端一致（`tools.*`/`profiles.*`/`settings.*`/`workdirs.*`/`sessions.list`/`terminal.*`/`app.info`/`launch.external`）。
