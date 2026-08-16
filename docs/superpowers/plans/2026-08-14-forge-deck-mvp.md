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
  Scanning/KnownDirsScanSource.cs 已知安装目录扫描
  Scanning/PathScanSource.cs     PATH 扫描
  Scanning/ExtraDirsScanSource.cs 附加目录扫描（规格 §4.1 数据源 #6，最低优先级）
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
- 创建：`src/ForgeDeck.Core/Scanning/KnownTools.cs`、`src/ForgeDeck.Core/Scanning/PathSearch.cs`、`src/ForgeDeck.Core/Scanning/IScanSource.cs`、`src/ForgeDeck.Core/Scanning/KnownDirsScanSource.cs`、`src/ForgeDeck.Core/Scanning/PathScanSource.cs`、`src/ForgeDeck.Core/Scanning/ExtraDirsScanSource.cs`、`src/ForgeDeck.Core/Scanning/ToolScanner.cs`
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

    private sealed class ThrowingSource : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) => throw new InvalidOperationException("源爆炸");
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
    public void Scan_ContinuesWhenSourceThrows()
    {
        var claude = FakeExe("claude.cmd");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new ThrowingSource(),
            new FakeSource(new ScanHit(claude, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        var tool = Assert.Single(tools);
        Assert.Equal("Claude Code", tool.Name);
        Assert.Equal("PATH", tool.Source);
    }

    [Fact]
    public void Probe_PrefersExtensionOverBareName()
    {
        var dir = Path.Combine(_dir, "shim");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");      // npm sh shim（无扩展名）
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var first = PathSearch.Probe(dir, "claude").First();
        Assert.EndsWith("claude.cmd", first);
    }

    [Fact]
    public void FindOnPath_PrefersCmdOverBareShim()
    {
        var dir = Path.Combine(_dir, "onpath");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir);
        try
        {
            var found = PathSearch.FindOnPath("claude");
            Assert.NotNull(found);
            Assert.EndsWith("claude.cmd", found);
        }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }

    [Fact]
    public void PathScanSource_FindsKnownTool_PrefersCmdOverBareShim()
    {
        var dir = Path.Combine(_dir, "onpath2");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir);
        try
        {
            var hit = Assert.Single(new PathScanSource().Scan(new ScanContext(Array.Empty<string>())));
            Assert.Equal("Claude Code", hit.Known!.Name);
            Assert.Equal("PATH", hit.SourceLabel);
            Assert.EndsWith("claude.cmd", hit.ExePath);
        }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }

    [Fact]
    public void MatchByName_PrefersLongerName()
    {
        var tool = KnownTools.MatchByName("Cursor Agent");
        Assert.NotNull(tool);
        Assert.Equal("Cursor Agent", tool.Name);
    }

    [Fact]
    public void KnownDirs_FindsToolInHintDir()
    {
        var hintDir = Path.Combine(_dir, "npm");
        Directory.CreateDirectory(hintDir);
        File.WriteAllText(Path.Combine(hintDir, "claude.cmd"), "");

        Environment.SetEnvironmentVariable("FD_TEST_NPM", hintDir);
        try
        {
            var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
                new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM%", "npm 全局") });
            var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
                new[] { "codex" }, Array.Empty<InstallHint>());
            var source = new KnownDirsScanSourceForTest(new[] { testTool, codexTool });

            // Codex 无 hint 不命中；Claude 命中 hint 目录
            var claudeHit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
            Assert.Equal("npm 全局", claudeHit.SourceLabel);
            Assert.EndsWith("claude.cmd", claudeHit.ExePath);
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_NPM", null); }
    }

    [Fact]
    public void ExtraDirs_FindsToolWithoutHint()
    {
        var extraDir = Path.Combine(_dir, "extra");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "codex.exe"), "");

        var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
            new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM_MISSING%", "npm 全局") });
        var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
            new[] { "codex" }, Array.Empty<InstallHint>());
        var source = new ExtraDirsScanSourceForTest(new[] { testTool, codexTool });

        // Claude 的 hint 目录不存在且附加目录里没有 claude；Codex 由附加目录兜底命中
        var hit = Assert.Single(source.Scan(new ScanContext(new[] { extraDir })));
        Assert.Equal("Codex CLI", hit.Known!.Name);
        Assert.Equal("附加目录", hit.SourceLabel);
        Assert.EndsWith("codex.exe", hit.ExePath);
    }
}

file sealed class KnownDirsScanSourceForTest(KnownTool[] tools) : KnownDirsScanSource
{
    protected override IEnumerable<KnownTool> Catalog => tools;
}

file sealed class ExtraDirsScanSourceForTest(KnownTool[] tools) : ExtraDirsScanSource
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
        All.Where(t => displayName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(t => t.Name.Length)
          .FirstOrDefault();
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
        // 先按扩展名探测（.exe/.cmd/.bat/.ps1），最后才尝试无扩展名直命中——
        // 避免 npm 全局目录的 sh shim（无扩展名）抢先命中导致启动失败。
        foreach (var ext in extensions)
        {
            var withExt = Path.Combine(dir, name + ext);
            if (File.Exists(withExt)) yield return withExt;
        }
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) yield return direct;
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

`src/ForgeDeck.Core/Scanning/KnownDirsScanSource.cs`（`Catalog` 虚属性供测试替换；只探测 hint 目录，附加目录已拆至 `ExtraDirsScanSource`——附加目录是规格 §4.1 的最低优先级数据源 #6，优先级由组合根注入顺序保证）：

```csharp
namespace ForgeDeck.Core.Scanning;

public class KnownDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInHints(tool);
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
}
```

`src/ForgeDeck.Core/Scanning/ExtraDirsScanSource.cs`（附加目录独立扫描源，标签"附加目录"，组合根中排在最后）：

```csharp
namespace ForgeDeck.Core.Scanning;

public class ExtraDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInExtraDirs(tool, context.ExtraDirs);
            if (hit != null) yield return hit;
        }
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

    /// <summary>sources 枚举顺序即优先级，先命中者胜（组合根注入顺序：KnownDirs→Path→Registry→StartMenu→ExtraDirs）。</summary>
    public ToolScanner(IEnumerable<IScanSource> sources) => _sources = sources;

    public List<ToolInfo> Scan(ScanContext context)
    {
        var byPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            List<ScanHit> hits;
            try
            {
                // 立即枚举，使源在枚举期间抛出的异常也纳入隔离范围（规格 §7）
                hits = source.Scan(context).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ForgeDeck] 扫描源 {source.GetType().Name} 失败，已跳过：{ex.Message}");
                continue;
            }
            foreach (var hit in hits)
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
        }
        return byPath.Values
            .OrderByDescending(t => t.Builtin)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter ToolScannerTests` → 预期 11 Passed。

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
- 修改：`tests/ForgeDeck.Core.Tests/ToolScannerTests.cs`（一个小改进，见步骤 5）

- [x] **步骤 1：编写失败的测试**

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
    public void RegistrySource_FallsBackToInstallLocation_WhenIconNotExecutable()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        var ico = Path.Combine(_dir, "cursor.ico");
        File.WriteAllText(ico, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorIco"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", ico);   // 指向 .ico：存在但非可执行 → 回落 InstallLocation
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
        Assert.Equal("Cursor", hit.Known!.Name);
        Assert.Equal("注册表", hit.SourceLabel);
        Assert.Equal(Path.GetFullPath(exe), hit.ExePath);
    }

    [Fact]
    public void RegistrySource_ToleratesNonStringRegistryValues()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorBadIcon"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", 5, RegistryValueKind.DWord);   // 畸形 REG_DWORD：不应抛 InvalidCastException
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
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

        var menuDir = Path.Combine(_dir, "StartMenu");
        var subDir = Path.Combine(menuDir, "Sub");   // 子目录：覆盖递归枚举路径
        Directory.CreateDirectory(subDir);
        var lnk2 = Path.Combine(subDir, "Claude2.lnk");
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

- [x] **步骤 2：实现两个扫描源**

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
                        item.GetValue("DisplayName") as string ?? "",
                        item.GetValue("InstallLocation") as string ?? "",
                        item.GetValue("DisplayIcon") as string ?? "");   // as：畸形 REG_DWORD 等不抛 InvalidCast
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
        // DisplayIcon 常指向 .ico/.dll 资源（如 "app.exe,0" / "app.ico" / "imageres.dll,-101"），
        // 仅当其为可启动扩展名且 exe 名与已知工具一致时直取，否则回落 InstallLocation 探测。
        var icon = entry.DisplayIcon.Split(',')[0].Trim().Trim('"');
        if (icon.Length > 0 && File.Exists(icon)
            && PathSearch.CliExtensions.Contains(Path.GetExtension(icon), StringComparer.OrdinalIgnoreCase)
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
        catch (Exception)
        {
            // 按单文件失败处理：ProgID 缺失（ArgumentNullException）、dynamic 绑定失败
            // （RuntimeBinderException）等环境异常不应冒泡废掉整个开始菜单源。
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
            // IgnoreInaccessible：跳过 ACL 拒绝的不可访问子目录，避免整源报废（被源级隔离静默吞掉）；
            // AttributesToSkip = 0 必须显式设置：默认 Hidden|System 会静默跳过隐藏属性的 .lnk，改变语义。
            var eo = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            };
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", eo))
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

实施备注：注册表/COM API 带 `[SupportedOSPlatform("windows")]`，纯 `net8.0` 目标会触发 CA1416 平台兼容警告（与「build 0 警告」门槛冲突）。故将 `src/ForgeDeck.Core/ForgeDeck.Core.csproj` 与 `tests/ForgeDeck.Core.Tests/ForgeDeck.Core.Tests.csproj` 的 TargetFramework 改为 `net8.0-windows`（App 本就是 `net8.0-windows`，产品为 Windows 专用启动器；净效果 0 警告，代码本身不变）。

- [x] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter RegistryAndStartMenuTests` → 预期 3 Passed；全量 `dotnet test` → 全绿（30+3=33 左右）；`dotnet build` → 0 警告。

- [x] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 注册表卸载项与开始菜单快捷方式扫描源"
```

- [x] **步骤 5（本任务追加）：ThrowingSource 迭代器化**

`tests/ForgeDeck.Core.Tests/ToolScannerTests.cs` 里的 `ThrowingSource` 当前是 `=> throw` 表达式体（调用即抛），改为迭代器形式（先 `yield return` 一条指向不存在路径的假 hit，再 `throw new InvalidOperationException("源爆炸")`），使 `Scan_ContinuesWhenSourceThrows` 真正覆盖 MoveNext 期间抛异常的路径。改完确认全量测试仍绿，随本任务一起提交：

```csharp
    private sealed class ThrowingSource : IScanSource
    {
        // 迭代器形式：先产出一条指向不存在路径的假 hit，再在枚举（MoveNext）期间抛异常，
        // 使 Scan_ContinuesWhenSourceThrows 覆盖 ToolScanner 立即枚举期间的异常隔离路径。
        public IEnumerable<ScanHit> Scan(ScanContext context)
        {
            yield return new ScanHit(Path.Combine(Path.GetTempPath(), "forgedeck-ghost.exe"), null, "爆炸源");
            throw new InvalidOperationException("源爆炸");
        }
    }
```

- [x] **步骤 6（质量审查修复）：开始菜单枚举容错与解析异常面收紧**

审查发现三处问题，均已修复并随 `fix(core): 开始菜单枚举容错与解析异常面收紧` 提交：

1. `StartMenuScanSource` 的 `Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories)` 遇不可访问子目录抛 `UnauthorizedAccessException` 且已产出为 0（整源报废、被源级隔离静默吞掉）。改用 `new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }`——`AttributesToSkip = 0` 必须显式设置（默认 Hidden|System 会静默跳过隐藏属性的 .lnk，改变语义）。
2. `WScriptShellLinkResolver.ResolveTarget` 的 catch 从 `COMException` 放宽为 `catch (Exception)`（ProgID 缺失/RuntimeBinderException 等环境异常按单文件失败处理，不应废源）；`RegistryScanSource` 的 `(string?)item.GetValue(...)` 改为 `item.GetValue(...) as string` 消除畸形 REG_DWORD 的 `InvalidCastException` 面。
3. 测试覆盖缺口补齐：`RegistrySource_FallsBackToInstallLocation_WhenIconNotExecutable`（DisplayIcon 指向 .ico → 回落 InstallLocation，红测试还暴露了 `MatchByExeName("cursor.ico")` 同名命中导致 .ico 被当作可执行返回的缺陷，已在 ResolveExe 加 `PathSearch.CliExtensions` 扩展名白名单）、`RegistrySource_ToleratesNonStringRegistryValues`（REG_DWORD 容错，红→绿证据）、`StartMenuSource_ResolvesLnkTarget` 的 .lnk 移入 menuDir 子目录（覆盖递归枚举路径）。

---

## 任务 8：启动服务（TDD）

**文件：**
- 创建：`src/ForgeDeck.Core/Launching/LaunchService.cs`
- 测试：`tests/ForgeDeck.Core.Tests/LaunchServiceTests.cs`

- [x] **步骤 1：编写失败的测试**

`tests/ForgeDeck.Core.Tests/LaunchServiceTests.cs`：

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    [InlineData(@"--model ""unclosed", new[] { "--model", "unclosed" })]
    [InlineData(@"/x """" /y", new[] { "/x", "", "/y" })]
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
    public void BuildCommand_UnsupportedExtension_Throws()
    {
        var py = Path.Combine(_dir, "tool.py");
        File.WriteAllText(py, "");
        Assert.Throws<NotSupportedException>(() => _service.BuildCommand(Tool(py), Profile()));
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
        _service.Validate(Tool(exe), Profile());
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
        // 外部启动：分词重组，含空白的参数重新引用（"sonnet 4" 不裂成两个参数）
        Assert.Equal("--model \"sonnet 4\"", psi.Arguments);
        Assert.Equal(_dir, psi.WorkingDirectory);
        Assert.Equal("V", psi.EnvironmentVariables["K"]);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void BuildExternalStartInfo_Ps1_WrapsWithPowerShellHost()
    {
        var script = Path.Combine(_dir, "tool.ps1");
        File.WriteAllText(script, "");
        var psi = _service.BuildExternalStartInfo(Tool(script), Profile("-Flag x", _dir));
        Assert.True(psi.FileName.Contains("pwsh") || psi.FileName.Contains("powershell"), $"实际 FileName: {psi.FileName}");
        Assert.Equal($"-File \"{script}\" -Flag x", psi.Arguments);
    }

    [Fact]
    public void BuildExternalStartInfo_ClaudeAutoRestore_AppendsResumeArgs()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var psi = _service.BuildExternalStartInfo(Tool(script), Profile("--model x", _dir, autoRestore: true));
        Assert.Equal("--model x --continue", psi.Arguments);
    }

    [Fact]
    public void LaunchExternal_CmdExitsWithCode()
    {
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var pid = _service.LaunchExternal(Tool(cmdPath), Profile("/c exit 3", _dir));
        Assert.Equal(3, WaitForExitCode(pid, 5000));
    }

    [Fact]
    public void LaunchExternal_Ps1Script_ExitsWithCode()
    {
        var script = Path.Combine(_dir, "exit5.ps1");
        File.WriteAllText(script, "exit 5");
        var pid = _service.LaunchExternal(Tool(script), Profile(workdir: _dir));
        Assert.Equal(5, WaitForExitCode(pid, 15000));
    }

    /// <summary>
    /// GetProcessById 得到的 Process 组件在 .NET 上不填充 ExitCode
    /// （抛 "Process was not started by this object"），故经句柄 P/Invoke 取退出码；
    /// 句柄须在等待退出之前取得（进程退出后 Handle 会重新 OpenProcess 并失败）。
    /// </summary>
    private static int WaitForExitCode(int pid, int timeoutMs)
    {
        using var process = Process.GetProcessById(pid);
        var handle = process.Handle;
        if (!process.WaitForExit(timeoutMs))
            throw new TimeoutException($"进程 {pid} 在 {timeoutMs}ms 内未退出");
        if (!GetExitCodeProcess(handle, out var code))
            throw new InvalidOperationException($"获取进程 {pid} 退出码失败");
        return code;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);
}
```

运行：`dotnet test --filter LaunchServiceTests` → 预期编译失败。

- [x] **步骤 2：实现 LaunchService**

`src/ForgeDeck.Core/Launching/LaunchService.cs`：

```csharp
using System.Diagnostics;
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

    /// <summary>PowerShell 宿主三级回退：PATH 上的 pwsh → PATH 上的 powershell → System32 全路径。</summary>
    private static string ResolvePowerShellHost() =>
        PathSearch.FindOnPath("pwsh")
        ?? PathSearch.FindOnPath("powershell")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powershell.exe");

    /// <summary>分词结果 + AutoRestore 追加的 ResumeArgs（已含则不重复）——内嵌/外部双轨共用。</summary>
    private static List<string> EffectiveArgs(ToolInfo tool, LaunchProfile profile)
    {
        var args = SplitArgs(profile.Args).ToList();
        var known = KnownTools.MatchByExeName(tool.ExePath);
        if (profile.AutoRestore && known?.ResumeArgs is { } resume && !args.Contains(resume))
            args.Add(resume);
        return args;
    }

    /// <summary>含空白的参数重新加引号（避免重组命令行时裂成多个参数）。</summary>
    private static string QuoteIfSpaced(string token) =>
        token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    public LaunchCommand BuildCommand(ToolInfo tool, LaunchProfile profile)
    {
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        var args = EffectiveArgs(tool, profile);
        return ext switch
        {
            ".exe" => new LaunchCommand(tool.ExePath, args),
            ".cmd" or ".bat" => new LaunchCommand(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                new[] { "/c", tool.ExePath }.Concat(args).ToList()),
            ".ps1" => new LaunchCommand(
                ResolvePowerShellHost(),
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
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        // 外部轨道：分词重组（AutoRestore 追加 ResumeArgs），含空白的参数重新引用；
        // .ps1 由 PowerShell 宿主包装执行（CreateProcess 无法直接执行脚本，与内嵌轨道语义一致）。
        var joined = string.Join(' ', EffectiveArgs(tool, profile).Select(QuoteIfSpaced));
        var psi = new ProcessStartInfo
        {
            FileName = ext == ".ps1" ? ResolvePowerShellHost() : tool.ExePath,
            Arguments = ext == ".ps1" ? $"-File \"{tool.ExePath}\"{(joined.Length > 0 ? " " + joined : "")}" : joined,
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

- [x] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter LaunchServiceTests` → 预期全部 Passed（含 6 条 InlineData，共 20 条）。

- [x] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 启动服务——校验/命令包装/env 展开/外部启动"
```

- [x] **步骤 5（审查修复追加）：ps1 外部启动宿主包装与双轨一致性**

审查发现四项问题，按 TDD 修复（上方代码块已为修复后最终版）：

1.（Important）`BuildExternalStartInfo` 对 `.ps1` 直接 FileName=ExePath，CreateProcess 无法执行（实测抛 Win32Exception）。修复：`.ps1` 外部启动由 PowerShell 宿主包装（`-File "脚本" 参数...`），宿主解析统一为三级回退 `ResolvePowerShellHost()`：PATH 上的 pwsh → PATH 上的 powershell → System32 全路径；`BuildCommand` 的 `.ps1` 分支同样改用三级回退（消除裸相对名 `powershell.exe`）。补真实进程集成测试：`LaunchExternal_Ps1Script_ExitsWithCode`（powershell -File 脚本 `exit 5`，验退出码）。
2.（Minor）`LaunchExternal_CmdExitsWithCode` 改为真正调用 `_service.LaunchExternal(tool, profile)`。注：`Process.GetProcessById(pid)` 的 `ExitCode` 在 .NET 上抛 "Process was not started by this object"（组件不填充退出码），且 `Handle` 须在等待退出前取得（进程退出后重新 OpenProcess 失败）——测试经 `WaitForExitCode` helper（GetProcessById + 先取句柄 + P/Invoke `GetExitCodeProcess`）等退出验码。
3.（Minor）AutoRestore 双轨一致：外部轨道 `Arguments` 改为 `SplitArgs` 分词（AutoRestore 追加 ResumeArgs）重组——含空白的参数经 `QuoteIfSpaced` 重新引用（否则 `"sonnet 4"` 裂成两个参数），空格 join。补断言：`BuildExternalStartInfo_ClaudeAutoRestore_AppendsResumeArgs`（Arguments == "--model x --continue"）。
4.（Minor）补测试：`BuildCommand_UnsupportedExtension_Throws`（.py 抛 NotSupportedException）；SplitArgs 未闭合引号与空引号对两条 InlineData。

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests docs/superpowers/plans
git commit -m "fix(core): ps1 外部启动宿主包装与双轨一致性"
```

---

## 任务 9：终端会话管理器（ConPTY，集成测试）

**文件：**
- 创建：`src/ForgeDeck.Core/Terminal/TerminalSessionManager.cs`
- 测试：`tests/ForgeDeck.Core.Tests/TerminalSessionManagerTests.cs`

- [x] **步骤 1：编写失败的集成测试**

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

    private static async Task<int> WaitForExitAsync(TerminalSessionManager mgr, string sessionId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExit(string id, int code) { if (id == sessionId) tcs.TrySetResult(code); }
        mgr.Exited += OnExit;
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        mgr.Exited -= OnExit;
        Assert.True(tcs.Task.IsCompleted, "超时未收到退出事件");
        return tcs.Task.Result;
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
    public async Task Create_SpaceInArgument_SurvivesCommandLine()
    {
        // verbatim 模式 Porta 不加引号，引号由管理器的 QuoteIfSpaced 自行添加；
        // 含连续双空格的参数作单 token 到达时，cmd echo 原样回显带引号原文（"forge  deck"）。
        // 实测：cmd echo 不重 join、不折叠空格——引号丢失（参数裂开）时回显不含引号，
        // 故断言带引号 + 双空格才真正可捕捉 QuoteIfSpaced 被误删的回归。
        var id = await _mgr.CreateAsync("echo2", CmdExe,
            new[] { "/c", "echo", "forge  deck" }, Path.GetTempPath());
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("\"forge  deck\""), TimeSpan.FromSeconds(10));
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task EnvVars_ReachChildProcess()
    {
        var id = await _mgr.CreateAsync("env", CmdExe, new[] { "/c", "echo %FD_TEST_A%" }, Path.GetTempPath(),
            env: new Dictionary<string, string> { ["FD_TEST_A"] = "forge-env-ok" });
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forge-env-ok"), TimeSpan.FromSeconds(10));
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
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
    public async Task Close_RemovesSessionFromList_AndKillsProcess()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        _mgr.Close(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
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

- [x] **步骤 2：实现会话管理器**

`src/ForgeDeck.Core/Terminal/TerminalSessionManager.cs`：

```csharp
using System.Collections;
using System.Text;
using Porta.Pty;

namespace ForgeDeck.Core.Terminal;

public sealed record TerminalSessionInfo(string SessionId, string Title, string Workdir, bool Running, int? ExitCode);

public sealed class TerminalSessionManager : IDisposable
{
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Session> _sessions = new();
    private readonly object _gate = new();

    /// <summary>终端输出（sessionId, chunk，UTF-8 已解码）。</summary>
    public event Action<string, string>? Output;
    /// <summary>进程退出（sessionId, exitCode；每个会话恰好触发一次）。</summary>
    public event Action<string, int>? Exited;
    /// <summary>会话列表或运行状态变化。</summary>
    public event Action? Changed;

    public async Task<string> CreateAsync(
        string title, string app, IReadOnlyList<string> args, string workdir,
        IReadOnlyDictionary<string, string>? env = null, int cols = 120, int rows = 30)
    {
        // 合并全量环境变量，避免子进程丢 PATH 等基础变量；
        // 忽略大小写去重（Windows 环境变量名不区分大小写），同名时用户值覆盖继承值
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string)e.Value!;
        if (env != null)
            foreach (var (key, value) in env)
                merged[key] = value;

        var id = Guid.NewGuid().ToString("N");
        // Porta.Pty 非 verbatim 模式会给每个参数加引号（含 "/c"），cmd.exe 无法解析；
        // 改用 verbatim + 仅给含空白的参数加引号（与 LaunchService.QuoteIfSpaced 约定一致），
        // App 路径的引号始终由 Porta 负责（带空格路径已验证）。
        var connection = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = title,
            Cols = cols,
            Rows = rows,
            Cwd = workdir,
            App = app,
            CommandLine = args.Select(QuoteIfSpaced).ToArray(),
            VerbatimCommandLine = true,
            Environment = merged,
        }, CancellationToken.None);

        var session = new Session(id, title, workdir, connection);
        lock (_gate) { _sessions[id] = session; }
        connection.ProcessExited += (_, e) => AnnounceExit(session, e.ExitCode);
        _ = PumpOutputAsync(session);
        // 极端竞态：进程在订阅事件前已退出（事件已丢）——主动探测补报，恰好一次语义由 AnnounceExit 保证
        try { if (connection.WaitForExit(0)) AnnounceExit(session, SafeExitCode(session)); }
        catch { }
        Changed?.Invoke();
        return id;
    }

    /// <summary>含空白的参数加引号，其余原样（Windows 命令行惯例）。</summary>
    private static string QuoteIfSpaced(string token) =>
        token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    /// <summary>标记退出并广播 Exited/Changed，恰好一次（Porta 事件与主动补报可能竞争）。</summary>
    private void AnnounceExit(Session session, int exitCode)
    {
        if (!session.TryMarkExited(exitCode)) return;
        Exited?.Invoke(session.Id, exitCode);
        Changed?.Invoke();
    }

    private static int SafeExitCode(Session session)
    {
        try { return session.Connection.ExitCode; }
        catch { return -1; }
    }

    private async Task PumpOutputAsync(Session session)
    {
        var buffer = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        // 有状态解码器：跨 chunk 的多字节 UTF-8 序列不会裂成 U+FFFD
        var decoder = Encoding.UTF8.GetDecoder();
        try
        {
            while (true)
            {
                var read = await session.Connection.ReaderStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read <= 0) break;
                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                if (count > 0)
                {
                    // 订阅者异常不得杀死输出泵（否则该会话剩余输出永久丢失）
                    try { Output?.Invoke(session.Id, new string(chars, 0, count)); }
                    catch { }
                }
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

    /// <summary>关闭并从列表移除会话（标签页 × 按钮）。kill/等退出/释放连接放后台执行：
    /// ① 立即 Dispose 会拆掉 Porta 的退出监视（Process.Exited 先退订），Exited 事件永远不发；
    /// ② 若在本方法内同步等退出，WaitForExit 会在调用线程上同步触发 Process.Exited——
    ///    而调用方往往在 Close 返回后才订阅 Exited，同步触发必然错过。后台化让事件一定晚于 Close 返回。</summary>
    public void Close(string sessionId)
    {
        Session? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session)) return;
            _sessions.Remove(sessionId);
        }
        _ = Task.Run(() =>
        {
            try
            {
                if (session.Running)
                {
                    session.Connection.Kill();
                    session.Connection.WaitForExit((int)CloseWaitTimeout.TotalMilliseconds);
                    // 事件未及送达（或 kill 失败）则补报，恰好一次
                    if (session.Running) AnnounceExit(session, SafeExitCode(session));
                }
            }
            catch { }
            finally { session.Dispose(); }
        });
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
        foreach (var s in all)
        {
            try
            {
                if (s.Running)
                {
                    s.Connection.Kill();
                    s.Connection.WaitForExit((int)CloseWaitTimeout.TotalMilliseconds);
                    if (s.Running) AnnounceExit(s, SafeExitCode(s));
                }
            }
            catch { }
            s.Dispose();
        }
    }

    private sealed class Session(string id, string title, string workdir, IPtyConnection connection) : IDisposable
    {
        private int _exitAnnounced;

        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Workdir { get; } = workdir;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public IPtyConnection Connection { get; } = connection;

        private bool _running = true;

        /// <summary>跨线程可见：ExitCode 先写、Running 后写（release），读侧见 Running=false 即可见 ExitCode。</summary>
        public bool Running
        {
            get => Volatile.Read(ref _running);
            private set => Volatile.Write(ref _running, value);
        }

        private int _exitCode = -1;
        public int ExitCode { get => _exitCode; private set => _exitCode = value; }

        /// <summary>记录退出状态；返回 false 表示已报过（防 Porta 事件与主动补报双发）。</summary>
        public bool TryMarkExited(int exitCode)
        {
            if (Interlocked.Exchange(ref _exitAnnounced, 1) == 1) return false;
            ExitCode = exitCode;
            Running = false;
            return true;
        }

        public void Dispose() => Connection.Dispose();
    }
}
```

- [x] **步骤 3：运行测试验证通过**

运行：`dotnet test --filter TerminalSessionManagerTests` → 预期 7 Passed（约 2-5 秒，含真实 ConPTY 进程）。

- [x] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): ConPTY 终端会话管理器（输出流/输入/resize/kill/close）"
```

- [x] **步骤 5（实现偏差记录，Porta.Pty 1.0.7 实测）：**

反编译/对照实验发现两处与 README 印象不符的实际行为，实现做了最小调整：

1. **参数引号**：非 verbatim 模式 Porta 给**每个**参数加引号（`"/c" "echo hi"`），cmd.exe 无法解析（实测 `'"echo hi' 不是内部或外部命令`、退出码 1；含空格 App 路径同样失败）。修复：`VerbatimCommandLine = true` + 自行按 Windows 惯例仅给含空白参数加引号（与 `LaunchService.QuoteIfSpaced` 一致）；App 路径引号由 Porta 始终负责（带空格路径实测通过）。
2. **Close 的 Dispose 顺序**：`PseudoConsoleConnection.Dispose()` 第一步就 `process.Exited -= handler`，Kill 后立即 Dispose 则 ProcessExited 永不触发；且 `Process.WaitForExit(ms)` 会在调用线程上**同步**触发 Process.Exited，而调用方在 `Close` 返回后才订阅 `Exited`——同步触发必然错过。修复：Close 移除会话后由后台任务执行 kill → 有界 WaitForExit(2s) → 补报（若事件未达）→ Dispose；`Session.TryMarkExited` 用 Interlocked 保证 Exited 恰好一次（Porta 事件与补报竞争安全）。
3. 顺带加固：输出泵用有状态 UTF-8 Decoder（跨 chunk 多字节序列不裂成 U+FFFD）；CreateAsync 订阅后 `WaitForExit(0)` 探测"订阅前已退出"的竞态并补报。

- [x] **步骤 6（质量审查加固）：**

1. 【Important】`Create_SpaceInArgument_SurvivesCommandLine` 断言强化：参数改含连续双空格（`forge  deck`）。实测 cmd echo **不重 join、不折叠空格**——引号丢失时双空格仍在，单靠 `Contains` 无法区分；而参数作单 token（带引号）到达时 echo 原样回显**含引号**原文，故断言定为 `Contains("\"forge  deck\"")`（带引号 + 双空格）。变异验证：临时删除 QuoteIfSpaced 的加引号逻辑，该测试立刻红。注释同步更正（verbatim 模式 Porta 不加引号，引号由 QuoteIfSpaced 自行添加）。
2. 【Minor】输出泵 `Output?.Invoke` 包 try/catch：订阅者异常不得杀死泵（否则该会话剩余输出永久丢失）。
3. 【Minor】merged 环境字典用 `StringComparer.OrdinalIgnoreCase` 初始化：避免 `Path`/`path` 类大小写变体重复键，同名时用户值覆盖继承值。
4. 【Minor】`Session.Running` 改 `Volatile.Read`/`Volatile.Write`：补齐跨线程可见性；写入顺序 ExitCode 先、Running（release）后，读侧见 `Running=false` 即可见最终 ExitCode。

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
    // TerminalCreate_WithCmdTool 会 spawn 真实进程：终端必须存字段并在 Dispose 释放
    private readonly TerminalSessionManager _terminal = new();
    // 可注入命中：rescan 复用测试需要扫描器返回既有工具同路径的命中
    private readonly List<ScanHit> _scanHits = new();
    private readonly ForgeDeckBridge _bridge = null!;

    public BridgeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[] { new FixedSource(_scanHits) }),
            _terminal);
    }

    public void Dispose()
    {
        _terminal.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FixedSource(List<ScanHit> hits) : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) => hits;
    }

    private static JsonElement ResultOf(string response)
    {
        // Clone 脱离文档生命周期：using 释放后返回的 JsonElement 仍可安全访问
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.GetProperty("result").Clone();
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
    public async Task HandleAsync_NullBodyOrNonStringMethod_ReturnsErrorNotThrow()
    {
        // 宿主 TryGetWebMessageAsString 可能返回 null；method 为数字时 GetString 会抛——都不得让异常逃逸
        var nullResp = await _bridge.Dispatcher.HandleAsync(null!);
        Assert.NotNull(nullResp);
        Assert.Equal("-32700", ErrorOf(nullResp!)!.Value.Code);

        var numMethodResp = await _bridge.Dispatcher.HandleAsync("""{"id":40,"method":123}""");
        Assert.NotNull(numMethodResp);
        Assert.Equal("-32602", ErrorOf(numMethodResp!)!.Value.Code);
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
            $$$"""{"id":3,"method":"tools.addManual","params":{"name":"X","exePath":"{{{Path.Combine(_dir, "ghost.exe").Replace("\\", "\\\\")}}}"}}""");
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
            $$$"""{"id":4,"method":"tools.addManual","params":{"name":"MyTool","exePath":"{{{exeJson}}}"}}""");
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

        // 事件转发：Output → terminal.data 事件封包（订阅须先于创建，首块输出可能立即可达）
        var outgoing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acc = "";
        void OnOutgoing(string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("event", out var ev) && ev.GetString() == "terminal.data")
                {
                    acc += doc.RootElement.GetProperty("data").GetProperty("chunk").GetString() ?? "";
                    if (acc.Contains("forge-bridge-e2e")) outgoing.TrySetResult(message);
                }
            }
            catch (JsonException) { }
        }
        _bridge.Dispatcher.Outgoing += OnOutgoing;
        try
        {
            var resp = await _bridge.Dispatcher.HandleAsync(
                """{"id":8,"method":"terminal.create","params":{"toolId":"tc1","cols":80,"rows":24}}""");
            var sessionId = ResultOf(resp!).GetProperty("sessionId").GetString();
            Assert.NotNull(sessionId);

            var done = await Task.WhenAny(outgoing.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(done == outgoing.Task, $"超时未收到 terminal.data 事件，累计输出：{acc}");
            using var payload = JsonDocument.Parse(outgoing.Task.Result);
            Assert.Equal("terminal.data", payload.RootElement.GetProperty("event").GetString());
            Assert.Equal(sessionId, payload.RootElement.GetProperty("data").GetProperty("sessionId").GetString());

            var listResp = await _bridge.Dispatcher.HandleAsync("""{"id":9,"method":"sessions.list"}""");
            Assert.Equal(1, ResultOf(listResp!).GetArrayLength());
            // lastUsed 与工作目录历史联动
            Assert.NotNull(_store.Config.LastUsed);
            Assert.Equal("tc1", _store.Config.LastUsed!.ToolId);
        }
        finally { _bridge.Dispatcher.Outgoing -= OnOutgoing; }
    }

    [Fact]
    public async Task Rescan_ReusesToolByPath_PreservesIdProfileAndLastUsed()
    {
        var cmdScript = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(cmdScript, "@echo off\r\n");
        _store.Config.Tools.Add(new ToolInfo { Id = "keep1", Name = "Fake Claude", ExePath = cmdScript, Source = "测试" });
        await _bridge.Dispatcher.HandleAsync(
            """{"id":20,"method":"profiles.save","params":{"profile":{"id":"p20","toolId":"keep1","name":"默认","args":"","env":{},"workdir":"","openMode":"external","autoRestore":false}}}""");
        _store.Config.LastUsed = new LastUsedInfo { ToolId = "keep1", Workdir = _dir };

        // 同路径重扫：复用旧条目（Id 不变、展示字段刷新），profile/lastUsed 不失联
        _scanHits.Add(new ScanHit(cmdScript, null, "新扫描源"));
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":21,"method":"tools.rescan"}""");
        var list = ResultOf(resp!);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("keep1", list[0].GetProperty("tool").GetProperty("id").GetString());
        Assert.Equal("新扫描源", list[0].GetProperty("tool").GetProperty("source").GetString());

        var profileResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":22,"method":"profiles.get","params":{"toolId":"keep1"}}""");
        Assert.Equal("p20", ResultOf(profileResp!).GetProperty("id").GetString());

        Assert.Contains(_store.Config.Tools, t => t.Id == _store.Config.LastUsed!.ToolId);
    }

    [Fact]
    public async Task LaunchExternal_CmdExitZero_ReturnsPidAndRecordsUsage()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        _store.Config.Tools.Add(new ToolInfo { Id = "le1", Name = "cmd", ExePath = cmdExe, Source = "测试" });
        await _bridge.Dispatcher.HandleAsync(
            """{"id":31,"method":"profiles.save","params":{"profile":{"id":"p31","toolId":"le1","name":"默认","args":"/c exit 0","env":{},"workdir":"","openMode":"external","autoRestore":false}}}""");
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":32,"method":"launch.external","params":{"toolId":"le1"}}""");
        Assert.True(ResultOf(resp!).GetProperty("pid").GetInt32() > 0);
        Assert.NotNull(_store.Config.LastUsed);
        Assert.Equal("le1", _store.Config.LastUsed!.ToolId);
    }

    [Fact]
    public async Task TerminalWrite_UnknownSession_ReturnsSessionGone()
    {
        // 关标签瞬间在途 write/resize 是良性竞态：统一映射为 session-gone，前端静默忽略
        var writeResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":10,"method":"terminal.write","params":{"sessionId":"nope","data":"x"}}""");
        var (writeCode, _) = ErrorOf(writeResp!)!.Value;
        Assert.Equal("session-gone", writeCode);

        var resizeResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":11,"method":"terminal.resize","params":{"sessionId":"nope","cols":80,"rows":24}}""");
        var (resizeCode, _) = ErrorOf(resizeResp!)!.Value;
        Assert.Equal("session-gone", resizeCode);
    }

    [Fact]
    public async Task SettingsGetSave_RoundTrip()
    {
        var getResp = await _bridge.Dispatcher.HandleAsync("""{"id":12,"method":"settings.get"}""");
        var result = ResultOf(getResp!);
        Assert.True(result.GetProperty("commonDirs").GetArrayLength() > 0);

        var saveResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":13,"method":"settings.save","params":{"settings":{"defaultShell":"cmd","autoScanOnStartup":false,"extraScanDirs":["D:\\Tools"],"skipExitConfirm":true,"preferEmbedded":false,"maxWorkdirHistory":20}}}""");
        Assert.Equal("cmd", ResultOf(saveResp!).GetProperty("settings").GetProperty("defaultShell").GetString());
        Assert.False(_store.Config.Settings.AutoScanOnStartup);
        Assert.True(_store.Config.Settings.SkipExitConfirm);
    }

    [Fact]
    public async Task Workdirs_AddAndList()
    {
        await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":14,"method":"workdirs.add","params":{"path":"{{{_dir.Replace("\\", "\\\\")}}}"}}""");
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":15,"method":"workdirs.list"}""");
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

    /// <remarks>宿主侧调用链在 async void 消息事件里（UI 消息循环），任何异常逃逸都会崩进程：
    /// 入口空串兜底、method 类型校验、handler 异常封包、响应封包 try/catch，全程不抛。</remarks>
    public async Task<string?> HandleAsync(string json)
    {
        // 宿主 TryGetWebMessageAsString 可能返回 null/空白：统一按解析失败封包
        if (string.IsNullOrWhiteSpace(json)) return Error(null, "-32700", "请求不是合法 JSON");
        JsonElement? id = null;
        string? method = null;
        JsonElement? parameters = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, "-32600", "请求必须是 JSON 对象");
            if (root.TryGetProperty("id", out var idEl)) id = idEl.Clone();
            // method 非字符串（如数字）时 GetString 会抛 InvalidOperationException：按缺失处理
            if (root.TryGetProperty("method", out var mEl))
                method = mEl.ValueKind == JsonValueKind.String ? mEl.GetString() : null;
            if (root.TryGetProperty("params", out var pEl) && pEl.ValueKind != JsonValueKind.Null)
                parameters = pEl.Clone();
        }
        catch (JsonException)
        {
            // 解析失败时 id 必未提取，封包不含 id
            return Error(null, "-32700", "请求不是合法 JSON");
        }
        if (string.IsNullOrEmpty(method)) return Error(id, "-32602", "缺少 method");
        if (!_handlers.TryGetValue(method!, out var handler))
            return Error(id, "-32601", $"未知方法：{method}");

        object? result;
        try { result = await handler(parameters); }
        catch (BridgeException ex) { return Error(id, ex.Code, ex.Message); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ForgeDeck] 桥方法 {method} 失败：{ex.Message}");
            return Error(id, "internal", ex.Message);
        }
        if (id == null) return null;

        // 响应封包必须用 Utf8JsonWriter 回写原始 id：匿名对象序列化时 default 的 JsonElement 会抛；
        // 封包/序列化同样兜底为 internal
        try
        {
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ForgeDeck] 桥响应封包失败（{method}）：{ex.Message}");
            return Error(id, "internal", ex.Message);
        }
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

/// <summary>业务方法接线。线程模型：handler 由宿主在 UI 线程串行调用，ConfigStore 变更无需加锁；
/// 唯一例外 tools.rescan——扫描与合并已 Task.Run 化，且只读脱离 store 的快照、不触碰 ConfigStore，
/// 合并结果回 UI 线程写回。终端 Output/Exited/Changed 事件来自后台线程，经 Dispatcher.Emit 透传
/// （Emit 仅做序列化，不写共享状态）。</summary>
public sealed class ForgeDeckBridge
{
    public const string Version = "0.1.0";

    // 反序列化复用同一 options 实例，保留 STJ 元数据缓存（WriteIndented 对反序列化无影响，可复用 Opts 风格）
    private static readonly JsonSerializerOptions PayloadOpts = JsonOptions.Create(o => o.WriteIndented = false);

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

        Dispatcher.Register("tools.rescan", async _ =>
        {
            // 快照脱离 store（后台不得触碰 ConfigStore）；扫描与合并在线程池执行——
            // UI 线程同步扫描会冻结窗口并反压终端输出泵；合并结果在 UI 线程写回
            var snapshot = _store.Config.Tools.Select(CloneTool).ToList();
            var extraDirs = _store.Config.Settings.ExtraScanDirs.ToList();
            var merged = await Task.Run(() =>
                MergeScanResults(snapshot, _scanner.Scan(new ScanContext(extraDirs))));
            _store.Config.Tools = merged;
            _store.Config.LastScanAt = DateTime.UtcNow;
            _store.Save();
            return ToolsList();
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
            var profile = p?.GetProperty("profile").Deserialize<LaunchProfile>(PayloadOpts)
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
            var settings = p?.GetProperty("settings").Deserialize<AppSettings>(PayloadOpts)
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
                // powershell 分支复用 LaunchService 三级回退的第二、三级：PATH 上的 powershell → System32 全路径
                "powershell" => (PathSearch.FindOnPath("powershell")
                                 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powershell.exe"), "PowerShell"),
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
            // 参数提取在 try 外：畸形请求（缺属性）应报 internal，不得误标 session-gone
            var sessionId = p?.GetProperty("sessionId").GetString() ?? "";
            var data = p?.GetProperty("data").GetString() ?? "";
            try
            {
                _terminal.Write(sessionId, data);
            }
            catch (KeyNotFoundException)
            {
                // 关标签瞬间在途 write 是良性竞态：session-gone 供前端静默忽略，不弹错误
                throw new BridgeException("session-gone", "会话已关闭");
            }
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.resize", p =>
        {
            var sessionId = p?.GetProperty("sessionId").GetString() ?? "";
            var (cols, rows) = Size(p);
            try
            {
                _terminal.Resize(sessionId, cols, rows);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeException("session-gone", "会话已关闭");
            }
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

    /// <summary>重扫合并：按 ExePath（OrdinalIgnoreCase）复用旧条目，保留 Id 与 Manual 标记——
    /// 否则每次重扫重铸 Id，profile/lastUsed 会静默失联（autoScanOnStartup=true 时每次启动都被重置）。
    /// 复用条目刷新展示字段（Name/Type/Source/Builtin），新路径才铸造新条目；
    /// 非手动且未被扫到的旧条目随重扫淘汰（扫描是来源的事实来源）。仅触碰快照，不改 ConfigStore。</summary>
    private static List<ToolInfo> MergeScanResults(List<ToolInfo> oldTools, List<ToolInfo> found)
    {
        var oldByPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldTools)
            oldByPath[Path.GetFullPath(old.ExePath)] = old;

        var result = oldTools.Where(t => t.Manual).ToList();
        foreach (var fresh in found)
        {
            var path = Path.GetFullPath(fresh.ExePath);
            if (oldByPath.TryGetValue(path, out var existing))
            {
                existing.Name = fresh.Name;
                existing.Type = fresh.Type;
                existing.Source = fresh.Source;
                existing.Builtin = fresh.Builtin;
                if (!existing.Manual) result.Add(existing);
            }
            else
            {
                result.Add(fresh);
            }
        }
        return result;
    }

    private static ToolInfo CloneTool(ToolInfo t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Type = t.Type,
        ExePath = t.ExePath,
        Source = t.Source,
        Builtin = t.Builtin,
        Manual = t.Manual,
    };

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

运行：`dotnet test --filter BridgeTests` → 预期 14 Passed。
再跑全量：`dotnet test` → 预期全部 Passed。

- [ ] **步骤 4：Commit**

```bash
git add src/ForgeDeck.Core tests/ForgeDeck.Core.Tests
git commit -m "feat(core): 消息桥——JSON 分发器与全部业务方法"
```

### 审查修正（实现后回填，代码块已同步为最终版本）

1. **测试类释放终端**：`BridgeTests` 将 `TerminalSessionManager` 存为字段，`Dispose` 里先 `_terminal.Dispose()` 再删临时目录——`TerminalCreate_WithCmdTool` 会 spawn 真实进程，不释放会泄漏。
2. **session-gone 错误码**：`terminal.write`/`terminal.resize` 把 `KeyNotFoundException` 转为 `BridgeException("session-gone", "会话已关闭")`。关标签瞬间在途 write 是良性竞态，前端静默忽略该码不弹错误。测试相应改为 `TerminalWrite_UnknownSession_ReturnsSessionGone`（同时覆盖 resize），替代原断言 "internal" 的版本。
3. **createShell 的 powershell 分支**：与任务 8 的宿主解析统一，回退链改为 `PATH 上的 powershell → System32 全路径`（原计划回退到裸 `"powershell.exe"`，工作目录不在 PATH 时会启动失败）。
4. **计划代码两处笔误修复**（TDD 红灯阶段暴露）：
   - 测试里 `$$"""` 内插原始字符串中 JSON 结尾的 `}}` 与内插闭合定界符冲突（CS9007），三处改为 `$$$"""` + `{{{ }}}`；
   - `ResultOf` 在 `using` 释放文档后返回的 `JsonElement` 不可再访问（`ObjectDisposedException`），返回前需 `Clone()`。
5. **-32700 封包**：JSON 解析失败时 `id` 必未提取，错误封包显式用 `Error(null, ...)`（不含 id），与协议一致。
6. 组合根顺序（任务 11 接线参考）：KnownDirs → Path → Registry → StartMenu → ExtraDirs。

### 二次审查修正（健壮性，代码块已同步为最终版本）

1. **HandleAsync 异常逃逸口封死**（宿主 async void 消息链上逃逸会崩进程）：入口空串/null 兜底为 -32700（`TryGetWebMessageAsString` 可能返回 null）；`method` 非 JSON 字符串时按缺失处理（-32602），不再让 `GetString()` 抛 `InvalidOperationException`；成功响应的 Utf8JsonWriter 封包段也包 try/catch 兜底为 internal。测试 `HandleAsync_NullBodyOrNonStringMethod_ReturnsErrorNotThrow`。
2. **tools.rescan 异步化**：handler 改 async，扫描与合并包 `await Task.Run`——UI 线程同步扫描会冻结窗口并反压终端输出泵；后台只读脱离 store 的快照（`CloneTool`），不触碰 ConfigStore，合并结果回 UI 线程写回。
3. **重扫按 ExePath 复用工具条目**：`MergeScanResults` 按 `ExePath`（OrdinalIgnoreCase）命中旧条目即复用（Id/Manual 保留），仅刷新展示字段；新路径才铸造新 Id。否则每次重扫（含 autoScanOnStartup）重铸 Id，profile/lastUsed 静默失联。测试 `Rescan_ReusesToolByPath_PreservesIdProfileAndLastUsed`。
4. **补核心价值测试**：`TerminalCreate_WithCmdTool` 订阅 `Dispatcher.Outgoing` + TaskCompletionSource，断言 2s 内收到含该 sessionId 的 `terminal.data` 事件封包；`LaunchExternal_CmdExitZero_ReturnsPidAndRecordsUsage`（cmd.exe `/c exit 0`）断言 pid>0 且 LastUsed 更新。
5. **write/resize 参数提取移出 try**：畸形请求（缺属性）报 internal，不误标 session-gone；resize 的 cols/rows 复用 `Size()` helper（消除死代码 `GetInt32() ?? 80`）。
6. **PayloadOpts 静态只读实例**：profiles.save/settings.save 反序列化复用同一 options，保留 STJ 元数据缓存。
7. **internal 错误落日志**：dispatcher 的 handler 异常与封包异常均 `Console.Error.WriteLine`（与 ToolScanner 一致）。
8. **线程模型固化**：ForgeDeckBridge 类头注释说明 handler 默认 UI 线程串行、rescan 后台化不触碰 ConfigStore、终端事件经 Emit 透传无共享状态写入。

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
                new ExtraDirsScanSource(),   // 规格 §4.1 数据源 #6：附加目录，最低优先级
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
- 修改：`ui/src/App.tsx`、`ui/src/bridge.ts`（Mock 两处保真度修复）

- [x] **步骤 1：TerminalPanel 组件**

`ui/src/TerminalPanel.tsx`（最终实现；oklch 主题经 Chromium 151 家族真机渲染验证通过，保留 oklch，十六进制近似值留注释备用——xterm v6 默认 DOM 渲染器走 CSS 颜色，原生支持 oklch。含二审修复：早到分块缓冲 pendingChunks，createShell 响应→refreshSessions 往返→effect 建实例窗口期内到达的 terminal.data 先缓存、实例创建后按序 flush，避免快速输出工具丢首块）：

```tsx
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
```

- [x] **步骤 2：App 接入会话状态**

`ui/src/App.tsx` 新增 import：

```tsx
import { useCallback, useEffect, useState } from 'react';
import { bridge } from './bridge';
import { TerminalPanel } from './TerminalPanel';
import type { TerminalSessionInfo } from './types';
```

组件体内新增状态与处理（最终实现。注意：**不要**用独立的自动选中 effect——`handleNewShell` 的 `setActiveSessionId(newId)` 提交时 sessions 还是旧列表（refreshSessions 往返未完成），effect 会误判 newId 失效而回退到旧首标签，导致新建第 2+ 个标签不再自动激活。失效校正统一放在数据到达期，用函数式 setState 原子判定：初始选首个、关激活标签选剩余首个、全关归 null、新建保留显式 set）：

```tsx
const [sessions, setSessions] = useState<TerminalSessionInfo[]>([]);
const [activeSessionId, setActiveSessionId] = useState<string | null>(null);

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
```

终端占位 `<section className="terminal">…</section>` 替换为：

```tsx
<TerminalPanel sessions={sessions} activeId={activeSessionId}
  onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
```

附带修复（`ui/src/bridge.ts` MockBridge 两处保真度问题）：
1. `sessions.list` 原返回内部数组的同一引用，React `setSessions` 因引用相同跳过重渲染、`[sessions]` effect 依赖不触发（真实桥每次返回新 JSON 数组，不受影响）。改为 `return [...this.sessions]; // 真实桥每次返回新 JSON 数组；副本保证 React 依赖比较生效`。
2. `mockOutput` 首块从 300ms 改为 0ms 立即发射：模拟真实 ConPTY 在 createShell 响应与 sessions 列表往返之前就开始输出，使浏览器调试能真实考验 TerminalPanel 的早到分块缓冲（`i === 0 ? 0 : 300`）。

- [x] **步骤 3：构建与验证**

1. `cd ui && npm run build` → `✓ built`（xterm 体积触发 >500kB chunk 提示，属预期告警）；`npm run lint` 0 警告 0 错误；`dotnet test` 76/76 绿。
2. WebView2 真机联调在本机受阻（环境问题，非代码问题）：msedgewebview2 浏览器进程在任何用户态应用下都无法派生（独立 WinForms+WebView2 最小复现确认，`CreateCoreWebView2ControllerAsync` 永久挂起、零子进程；同机 SearchHost/GameViewer 等系统级 WebView2 实例正常；headless msedge 本身可运行）。已尝试：--proxy-server=direct（旁路系统代理 127.0.0.1:10808 对 localhost 的劫持）、vite 绑定 127.0.0.1（原仅 ::1）、WMI/计划任务脱离进程树启动，均无法绕过。后端 ConPTY 全链已由 76 个单测覆盖。
3. 改用 headless Edge（Chromium 151 家族，与 WebView2 151 同引擎）+ CDP 驱动 vite dev server（MockBridge）完成 UI 真机验证：
   - 连点两次 `+` → 第二个标签立即且稳定保持激活（时序回归修复验证）；慢速开第三个同样自动激活；
   - 两标签的 xterm 文本均完整含首行 mock 输出（0ms 首块早于实例创建，缓冲不丢数据验证）；
   - 点 × 关闭激活标签 → 自动重选剩余首个，标签与 term-body 实例销毁、无残留 DOM；
   - 视口 754→900→1600 宽三档 → xterm 行宽随容器等比变化（fit 重排生效）；全部关闭 → 空态正常；
   - 全程 console 无 error、无未捕获异常；oklch 主题实际渲染为深绿黑底 + 浅绿白字，与设计稿协调，无黑块/默认色异常——**最终保留 oklch**。
   - pwsh 真实提示符/echo 回显路径未能在 WebView2 内直测（见 2），该链路由后端单测覆盖，待环境修复后补一次人工冒烟。

- [x] **步骤 4：Commit**

```bash
cd /c/workspace/ForgeDeck && git add ui/src && git commit -m "feat(ui): 内嵌终端面板——xterm 多会话/自适应尺寸/输入输出流"
# 二审修复（激活时序回归 + 早到输出缓冲）：
git commit -m "fix(ui): 会话激活时序回归与早到输出缓冲"
```

---

## 任务 13：快速启动页（指标 + 工具列表 + 手动添加）

**文件：**
- 创建：`ui/src/LauncherView.tsx`、`ui/src/ToolListPanel.tsx`、`ui/src/Modal.tsx`、`ui/src/AddToolModal.tsx`、`ui/src/lib/format.ts`
- 修改：`ui/src/App.tsx`（本任务引入数据加载主线，替换 launcher 占位）

- [x] **步骤 1：工具函数**

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

- [x] **步骤 2：Modal 基座（含 Esc/关闭动画）**

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

- [x] **步骤 3：AddToolModal**

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

- [x] **步骤 4：ToolListPanel**

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

- [x] **步骤 5：LauncherView（指标区 + 双栏骨架）**

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

- [x] **步骤 6：App 接入数据主线**

`ui/src/App.tsx` 更新（关键增量；与任务 12 的会话代码合并后的完整形态）。

**实现时修正（相对下文初稿，最终代码块已同步为实际实现）：**

1. `refreshSessions` 保留任务 12 的实现（含 activeId 校正逻辑），不回退为初稿的简单版本；因此初稿里多余的 `useEffect(activeSessionId == null → 选首个)` 补正 effect 删除——任务 12 的 refreshSessions 已在数据到达时校正。
2. 任务 12 的 `useEffect(() => { refreshSessions(); })` 与 `useEffect(() => bridge.on('sessions.changed', ...))` 并入本步骤的单一启动 effect（订阅在 effect 体内同步注册，异步加载期间的 sessions.changed 事件不丢），避免启动期重复请求。
3. `handleNewShell` 保留任务 12 引入的 try/catch + console.error（任务 16 换 Toast）。
4. `workdirs`/`profile` 的值在本任务尚无读取方（任务 14 的 FolderPicker/ConfigPanel 才读），strict + noUnusedLocals/oxlint no-unused-vars 会报错——暂以 `_workdirs`/`_profile` 命名豁免并加注释，任务 14 恢复具名读取。
5. rescan 分支内层 `setAppInfo(…)` 增加 `if (!disposed)` 守卫；`finally` 复位 scanning 覆盖失败/卸载路径。

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
```

- [x] **步骤 7：构建与联调验证**

运行：`cd ui && npm run build` → `✓ built`。
联调验证：启动后自动扫描（真实机器应识别出本机已装的已知工具）；三个指标卡显示真实数据；点"手动添加工具"填一个不存在的路径 → 弹窗内显示错误；填 `C:\Windows\System32\cmd.exe` 名称 `Cmd 手动` → 列表新增；顶栏刷新按钮触发重扫。

- [x] **步骤 8：Commit**

```bash
git add ui/src
git commit -m "feat(ui): 快速启动页——指标/工具列表/扫描/手动添加"
```

---

## 任务 14：启动配置面板与工作目录控件

**文件：**
- 创建：`ui/src/ConfigPanel.tsx`、`ui/src/WorkdirControl.tsx`、`ui/src/FolderPickerModal.tsx`、`ui/src/Switch.tsx`、`ui/src/lib/env.ts`
- 修改：`ui/src/App.tsx`（替换占位 configPanel、接启动/保存流程）

> **修正项（相对本计划初稿，源于任务 12/13 审查演进）：**
> 1. App.tsx 里任务 13 暂以 `_workdirs`/`_profile` 命名豁免的值恢复具名使用（workdirs 传 ConfigPanel 与 FolderPickerModal、profile 传 ConfigPanel）。
> 2. selectTool 竞态防护（任务 13 审查 Minor 3）：`latestToolIdRef` 记录最近请求的 toolId，`profiles.get` 响应经 `setProfile((cur) => ref === p.toolId ? p : cur)` 校验，快速连点时过期 profile 被丢弃。`handleSaveProfile` 的保存响应同样按 id 校验（同类竞态：保存后立即切工具）。
> 3. ConfigPanel 草稿复位 effect 依赖 `[profile.id]`（按值依赖会在保存回写时清掉用户正在编辑的草稿），oxlint exhaustive-deps 警告以 eslint-disable 块注释豁免；workdir 单独 effect 依赖扩展为 `[profile.id, profile.workdir]`——含 id 是因为工具切换时两个 profile 的 workdir 值可能相同（如均为空），仅依赖值会保留上一工具的未保存草稿（串档）。
> 4. 嵌入式启动后 `setActiveSessionId(sessionId)`：任务 12 的 refreshSessions 函数式校正不会覆盖列表中仍存在的 cur，新标签稳定激活。

- [x] **步骤 1：env 文本解析**

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

- [x] **步骤 2：Switch 与 WorkdirControl**

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

- [x] **步骤 3：FolderPickerModal**

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

- [x] **步骤 4：ConfigPanel**

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

  // 切换 profile（id 变化）时复位本地草稿；保存回写不换 id，草稿得以保留——其余字段为有意忽略。
  // 按计划依赖 [profile.id]，exhaustive-deps 警告在此禁用（oxlint 兼容 eslint-disable 注释）。
  /* eslint-disable react-hooks/exhaustive-deps */
  useEffect(() => {
    setArgs(profile.args);
    setEnvText(stringifyEnv(profile.env));
    setAutoRestore(profile.autoRestore);
    setOpenMode(profile.openMode);
  }, [profile.id]);
  /* eslint-enable react-hooks/exhaustive-deps */

  // 工作目录单独跟随：文件夹选择弹窗直接更新 App 层 profile，需要立即回显。
  // 依赖含 profile.id：工具切换时即使两个 profile 的 workdir 值相同（如均为空）也必须复位，避免草稿串档
  useEffect(() => setWorkdir(profile.workdir), [profile.id, profile.workdir]);

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

- [x] **步骤 5：App 接入配置面板与启动流程**

`ui/src/App.tsx`：新增 import（`ConfigPanel`、`FolderPickerModal`）与 `useRef`；任务 13 的 `_workdirs`/`_profile` 恢复具名（`workdirs`/`profile`）；新增状态 `const [pickerOpen, setPickerOpen] = useState(false);`；替换任务 13 的 configPanel 占位：

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

新增/修改处理函数（selectTool 加竞态防护，见任务头部修正项 2/4）：

```tsx
// 竞态防护：快速连点工具时 profiles.get 响应可能乱序到达，回调式校验丢弃非最新请求的过期 profile
const latestToolIdRef = useRef<string | null>(null);

const selectTool = useCallback(async (toolId: string) => {
  latestToolIdRef.current = toolId;
  setSelectedToolId(toolId);
  const p = await bridge.request<LaunchProfile>('profiles.get', { toolId });
  setProfile((cur) => (latestToolIdRef.current === p.toolId ? p : cur));
}, []);

const handleSaveProfile = useCallback(async (p: LaunchProfile) => {
  const saved = await bridge.request<LaunchProfile>('profiles.save', { profile: p });
  // 同 selectTool 的竞态防护：保存响应晚于工具切换到达时（cur 已是其他工具）丢弃，避免覆盖新选中项
  setProfile((cur) => (cur && cur.id === saved.id ? saved : cur));
}, []);

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
    }
    setAppInfo(await bridge.request<AppInfo>('app.info'));
    await refreshWorkdirs();
  } catch (e) { console.error('启动失败', e); } // Toast 在任务 16 接入
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


- [x] **步骤 6：构建与联调验证**

1. `cd ui && npm run build` → `✓ built`；`npm run lint` → 0 警告 0 错误（ConfigPanel 的草稿复位 effect 按计划依赖 `[profile.id]`，exhaustive-deps 警告以 eslint-disable 块注释豁免）。
2. 沿用任务 12 的替代方案：headless Edge（Chromium 151）+ CDP 驱动 vite preview（MockBridge），19 项断言全过：
   - 选中 Codex CLI → 配置面板渲染（logo `Co`/名称/`codex.exe` 文件名）；参数、workdir、env 文本域均可编辑（env 混入注释行与无 `=` 行验证解析）；
   - workdir 下拉 → 最近目录菜单显示 mock 3 条，点选回填输入框、菜单关闭；文件夹按钮 → FolderPickerModal 打开，常用位置 = 历史 3 + commonDirs 4 去重合并 7 条（≤12），点选联动 path 输入与 active 高亮，确认后弹窗关闭且 workdir 输入框**立即回显**（App 层 profile → ConfigPanel workdir effect 生效）；
   - 运行方式二选一切换（内嵌终端/独立窗口）正常；Claude Code（builtin）显示"启动时自动恢复上次会话"开关且可切换，Codex 不显示，手动添加的同名 "Claude Code"（非 builtin）也不显示——RESUMABLE 集合 + builtin 条件均验证；
   - 保存 → panel-meta 短暂"已保存"（1.4s 后复位"未保存更改"）；启动（embedded）→ mock 下新终端标签出现并激活；
   - **竞态防护对照实验**：先给 Claude/Codex 分别保存 args `AAA`/`BBB`，快速连点两者，4ms 间隔采样 200ms 窗口——带防护时全程 `BBB`（过期 `AAA` 从未出现）、标题跟随最后点击项；临时禁用防护重跑同一采样可捕获 `AAA`（t≈89–100ms 可见过期 profile），证明测试有判别力、防护实际生效；
   - 布局探针对照设计稿第 62-63 行：config-top（logo+名称+文件名）、三段 config-section（启动参数/环境变量/运行方式）、workdir-control 三列 grid 实测 `527px 34px 34px`、choice-row 双等宽列、config-actions（primary 启动工具 + 保存配置）、switch 34×20，面板与页面均无横向溢出。
3. 真实 WebView2 宿主联调（真实 cmd `/k` 存活、独立窗口弹出、重启后配置持久化）受任务 12 所述环境问题所限未执行，由后端 76 个单测覆盖桥接语义，待环境修复后补人工冒烟；未选工具 fallback 面板在 mock 下不可触发（启动即自动选中 preferred），仅经代码路径确认。

- [x] **步骤 7：Commit**

```bash
cd /c/workspace/ForgeDeck && git add ui/src docs/superpowers/plans/2026-08-14-forge-deck-mvp.md
git commit -m "feat(ui): 启动配置面板——参数/工作目录控件/环境变量/运行方式/启动流程"
```

---

## 任务 15：工具库、终端会话与设置视图

**文件：**
- 创建：`ui/src/ToolsView.tsx`、`ui/src/SessionsView.tsx`、`ui/src/SettingsView.tsx`
- 修改：`ui/src/App.tsx`（替换三个占位视图）

> **修正项（相对本计划初稿，实现时确认）：**
> 1. SettingsView 的 `shell` 本地状态需显式 `useState<string>(...)`：初稿的 `useState(info.settings.defaultShell)` 会把状态收窄为 `'pwsh' | 'powershell' | 'cmd'` 联合类型，`setShell(e.target.value)`（string）过不了 tsc；到 `AppSettings['defaultShell']` 的收窄转换仍由 save 时的 `as` 承担。
> 2. `handleSaveSettings` 按任务 13/14 惯例包 try/catch + console.error（失败兜底静默，Toast 任务 16 接入）；成功后 `setSettingsInfo` 回写，SettingsView 的 `[info]` effect 据此把表单回显为保存后的值。
> 3. 设置仅影响新会话/新扫描（defaultShell 由后端在 `terminal.createShell` 读取、extraScanDirs 由扫描器读取），已开会话不受影响——无需前端额外代码，已确认。
> 4. mock 桥 `terminal.close` 与真实后端 `Close` 一致为**移除会话**（`_sessions.Remove`），"已退出"态仅 `terminal.kill`/进程退出可达（running=false 保留在列表）；浏览器验证用临时桥补丁走 kill 路径核对"已退出 · exitCode"渲染，验证后已还原。

- [x] **步骤 1：ToolsView**

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

- [x] **步骤 2：SessionsView**

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

- [x] **步骤 3：SettingsView**

`ui/src/SettingsView.tsx`：

```tsx
import { useEffect, useState } from 'react';
import { Switch } from './Switch';
import type { AppSettings, SettingsInfo } from './types';

export function SettingsView({ info, onSave }: { info: SettingsInfo; onSave: (s: AppSettings) => void }) {
  const [extraDirs, setExtraDirs] = useState(info.settings.extraScanDirs.join('\n'));
  const [autoScan, setAutoScan] = useState(info.settings.autoScanOnStartup);
  const [shell, setShell] = useState<string>(info.settings.defaultShell);
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

- [x] **步骤 4：App 替换占位视图**

`ui/src/App.tsx` 中三个占位 section（现为简单 main-head）替换为：

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

新增保存函数（含修正项 2 的 try/catch）：

```tsx
const handleSaveSettings = useCallback(async (settings: AppSettings) => {
  try {
    setSettingsInfo(await bridge.request<SettingsInfo>('settings.save', { settings }));
  } catch (e) {
    console.error('保存设置失败', e); // Toast 在任务 16 接入
  }
}, []);
```

（补 import：`ToolsView`、`SessionsView`、`SettingsView`、类型 `AppSettings`。）

- [x] **步骤 5：构建与联调验证**

1. `cd ui && npm run build` → `✓ built`；`npm run lint` → 0 警告 0 错误。
2. 沿用任务 12/14 替代方案：headless Edge（Edg/151）+ CDP 驱动 vite preview（MockBridge，127.0.0.1），逐项验证全过：
   - **工具库**：表头 5 列（工具/可执行文件/来源/默认方式/状态），mock 4 行渲染（Claude Code=内嵌终端、Cursor Agent=独立窗口、均"已安装"）；点"扫描本机工具"→ 视图切回 launcher 且扫描态触发（40ms 间隔采样：t=40–160ms "正在扫描…"、重新扫描按钮 disabled，t=200ms 起"自动扫描 · 已完成"）。
   - **会话**：初始空态文案"暂无会话。启动工具或新建空白会话后在此查看。"；新建空白会话 → 卡片出现（标题 pwsh/workdir/运行中），终端面板同步出标签；点标签 × → mock close **移除**会话（与真实后端 Close 语义一致），卡片消失、空态恢复；"已退出"渲染经临时桥补丁走 `terminal.kill`（running=false + exitCode=1）核对 → 卡片保留且显示"已退出 · 1"，验证后补丁已还原（见修正项 4）。
   - **设置**：两卡（工具发现/终端偏好）+ 附加扫描目录文本域 + 三开关（启动时自动扫描=on/关闭应用时不弹会话确认=off/优先使用内嵌终端=on）+ 默认 Shell 输入渲染；改 defaultShell 为 `cmd`、填两行附加目录、关自动扫描 → 保存 → `settings.get` 回读 `defaultShell=cmd`、`autoScanOnStartup=false`、`extraScanDirs` 两行按 trim/去空行解析，且输入框/开关立即回显新值（SettingsView `[info]` effect 生效；**任务 16 补注（误归因修正）**：mock 的 `settings.save` 返回的是同一 `this.settings` 对象引用，React 同引用 bailout 下 `setSettingsInfo` 不触发重渲染、`[info]` effect 实际不会重跑——当时的"立即回显"来自表单本地草稿本身。该 effect 路径仅在真实桥下成立（每次响应均为新反序列化对象）），未提交字段（maxWorkdirHistory 等）经 `...info.settings` 展开保留。
   - **term-hidden 回归**：工具库与设置视图根节点均为 `app term-hidden`，会话视图为 `app`（按设计保留终端面板）。
   - **视觉对照**（设计稿 66-83 行）：三视图截图经视觉模型核对——eyebrow/title/sub/按钮文案、表格结构与 4 行数据、会话卡（标题+mono 路径+状态）、设置卡（文本域/输入框/三开关/右下 primary 保存按钮）均与设计稿一致；布局探针：三视图均无横向溢出，session-grid/settings-grid 实测两等宽列（478px×2），data-table 968px 贴合 970px 面板。
3. mock 限制（注明）：`settings.save` 仅持久于内存，页面刷新即重置（刷新后实测 autoScan 回 true、defaultShell 回 pwsh），"关闭自动扫描 → 刷新后启动走 tools.list"与"附加扫描目录 → 重扫出现新工具（来源=附加目录）"两条依赖持久化/真实扫描器的链路无法在 mock 下验证，由后端单测（ConfigStore 持久化、扫描器 extraScanDirs 合并）覆盖，任务 16 真机验收时复核；mock 的 `terminal.createShell` 标题硬编码 pwsh，"改 cmd → 新会话标题变 cmd"同属真机项（真实桥读 `Settings.DefaultShell`）。

- [x] **步骤 6：Commit**

```bash
cd /c/workspace/ForgeDeck && git add ui/src docs/superpowers/plans/2026-08-14-forge-deck-mvp.md
git commit -m "feat(ui): 工具库/终端会话/设置视图接入真实数据"
```

---

## 任务 16：错误 Toast、体验收尾与验收

**文件：**
- 创建：`ui/src/Toast.tsx`
- 修改：`ui/src/App.tsx`（toast 接线）、`ui/src/TerminalPanel.tsx`（session-gone 静默判定）、`ui/src/app.css`、`ui/src/TopBar.tsx`、`ui/src/ConfigPanel.tsx`、`ui/src/AddToolModal.tsx`、`ui/src/lib/env.ts`、`ui/src/WorkdirControl.tsx`、`ui/src/SettingsView.tsx`、`src/ForgeDeck.Core/Scanning/StartMenuScanSource.cs`、`README.md`

- [x] **步骤 1：Toast 组件**（按计划实现，无偏差）

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

- [x] **步骤 2：App 接线**（按计划；偏差与补充：catch 分支保留 console.error 同时加 toast；`handleAddTool` 失败 toast 后 rethrow 由弹窗就地展示同一错误并复位提交态；启动 effect/useCallback 依赖数组补 `toast`）

`ui/src/App.tsx`：

```tsx
const [toasts, setToasts] = useState<ToastItem[]>([]);
const toast = useCallback((text: string, kind: ToastItem['kind'] = 'info') => {
  const item: ToastItem = { id: Date.now() + Math.random(), text, kind };
  setToasts((prev) => [...prev, item]);
  setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== item.id)), 3200);
}, []);
```

错误路径：启动加载 catch、`handleRescan`/`handleNewShell`/`handleSaveSettings`/`handleLaunch`/`handleSaveProfile` 的 catch → `toast(e.message, 'error')`；`handleAddTool` 失败 → toast + rethrow（弹窗内就地展示）。成功 toast：`handleSaveProfile` → `'已保存'`、`handleSaveSettings` → `'设置已保存'`、`handleAddTool` → `'已添加到工具库'`、独立窗口启动 → `` `已在独立窗口打开 ${tool.tool.name}` ``。渲染 `<Toast items={toasts} />` 于根 div 末尾。

TerminalPanel 补充：write/resize 的 catch 收敛为 `ignoreSessionGone`——`session-gone` 前缀（关标签瞬间在途请求的良性竞态）静默，其余打 console.error（handleLaunch 的 embedded 分支无需特判，create 不会返回该码）。

- [x] **步骤 3：验收**

本机 WebView2 已知异常，WPF 宿主真机清单跳过，以「全量测试 + 构建产物 + 浏览器断言」替代执行：

1. `dotnet test` → 76/76 Passed；`dotnet build --no-incremental` → 0 警告 0 错误；`cd ui && npm run build` → ✓ built；`npm run lint` → 0 warnings 0 errors。
2. 构建产物：`ui/dist`（index.html + assets/index-*.js|css）完整复制入 ForgeDeck.App 输出 wwwroot。
3. 浏览器验收（headless Edge + CDP + 伪 WebView2 桥注入：比 MockBridge 更强，可检查全部请求载荷并按方法注入错误响应），26 项断言 + 1 项专项全过：
   - 启动成功/rescan 成功/内嵌启动成功 均无 toast（设计如此，成功 toast 仅四处：保存配置/保存设置/添加工具/独立窗口）。
   - AddToolModal：无效提交就地展示（弹窗内、无桥请求、无 toast）；桥失败（注入）→ 一条错误 toast + 弹窗就地展示同一错误 + 提交态复位；添加成功 → toast 一次 + 载荷正确 + 列表 5 行。
   - env 值 trim：`KEY = value` 载荷 `{KEY:'value'}`。
   - 连续两次保存 flash 令牌化：第二次 flash 存活 >1.15s、~1.4s 熄灭（无令牌时会在 ~1.1s 被第一次的定时器误关）。
   - 非法 shell（bash）→ 载荷 pwsh、输入框保留原文；` cmd ` → 载荷 cmd。
   - term-hidden 视图 toast-wrap bottom=18px，常规视图 280px。
   - 错误 toast（rescan/settings.save/app.info 断桥注入）：文案 `code: message`、红边 rgb(229,72,77)（--danger）、3.2s 自动消失（实测 3211ms）、console.error 保留。
   - session-gone 在途写入静默（0 console.error），非 session-gone 写失败打 console.error。
   - 通知装饰按钮 tabIndex=-1；workdir aria-controls 收起时不挂载、展开指向 workdirMenu。
4. 真机保留项（WebView2 环境恢复后复核）：真实扫描/ConPTY 输入输出/独立窗口 env 生效（`cmd /k set FOO=bar`）/自动恢复 `--continue` 回显/退出确认/响应式拖拽。

- [x] **步骤 3.5：审查遗留清理**（任务 16 扩展，一次小提交）

1. `app.css` 末尾追加 `.app.term-hidden .toast-wrap{bottom:18px}`；`:root` 增 `--danger:#e5484d`，`.toast.error` 边色与 AddToolModal 内联错误色改引用令牌。
2. `TopBar.tsx` 通知装饰按钮 `tabIndex={-1}`。
3. `ConfigPanel.tsx` save 改 async：`await onSave(current())` 后走 flash（失败不闪，错误 toast 由 App 层弹）；flash 用 `flashSeq` ref 序号令牌，定时器回调校验令牌才熄灭。`onSave` prop 类型放宽为 `void | Promise<void>`。
4. `lib/env.ts` parseEnvText 值侧 trim（key 侧不变）。
5. `WorkdirControl.tsx` `aria-controls` 仅 menuOpen 时挂载。
6. `SettingsView.tsx` shell 载荷校验：`['pwsh','powershell','cmd'].includes(shell.trim())` 否则回退 pwsh，输入框保留原文。
7. `StartMenuScanSource.cs` Scan 方法 40-52 行区域缩进对齐修复（纯格式）。
8. 任务 15 步骤 5 设置段补注 mock 同引用 bailout 误归因说明。
9. `README.md` 增「功能现状」一节。

- [x] **步骤 4：更新 README 并 Commit**（分两次提交）

```bash
git add ui/src/Toast.tsx ui/src/App.tsx ui/src/TerminalPanel.tsx
git commit -m "feat(ui): 错误 Toast 与成功反馈接线"
git add ui/src/app.css ui/src/TopBar.tsx ui/src/ConfigPanel.tsx ui/src/AddToolModal.tsx \
        ui/src/lib/env.ts ui/src/WorkdirControl.tsx ui/src/SettingsView.tsx \
        src/ForgeDeck.Core/Scanning/StartMenuScanSource.cs README.md \
        docs/superpowers/plans/2026-08-14-forge-deck-mvp.md
git commit -m "chore: 审查遗留清理——toast 定位/令牌化/可访问性/格式与文档"
```

（实际提交：第一笔 `3476062` feat(ui): 错误 Toast 与成功反馈接线；第二笔 chore: 审查遗留清理——toast 定位/令牌化/可访问性/格式与文档，即本计划文档所在的收尾提交，hash 见 git log。）

---

## 计划自检记录

- **规格覆盖度**：§2 目标 1→任务 6/7（扫描）+13（手动添加）；目标 2→任务 14（配置面板）；目标 3→任务 9/12（内嵌终端）；目标 4→任务 8/14（独立窗口）；目标 5→任务 5/14（工作目录历史）；目标 6→任务 15（设置页）。§3 桥接方法→任务 10 全量注册（`dialog.selectDirectory` 已按规格更新移除，改应用内选择器）。§4.1 数据源→任务 6/7；§4.2 配置→任务 4/5；§4.3 启动包装→任务 8；§4.4 终端→任务 9；§4.5 桥→任务 10/11。§5 前端四视图+弹窗+Mock→任务 1/2/3/12/13/14/15。§6 流程→任务 13/14 启动主线；§7 错误→任务 10（错误封包）/11（退出确认）/16（Toast）+任务 4（损坏恢复）；§8 测试→各任务 TDD 步骤。无遗漏。
- **占位符扫描**：无"待定/TODO"；任务 13 的 configPanel 占位是任务内的显式中间态，任务 14 替换为真实现，链路闭合。
- **类型一致性**：`LaunchProfile.autoRestore`（任务 4 定义，8/10/14 使用）；`ScanHit(ExePath, Known, SourceLabel)`（任务 6 定义，7 使用）；`TerminalSessionInfo(SessionId, Title, Workdir, Running, ExitCode)`（任务 9 定义，10/12/15 使用）；前端 `ToolListItem{tool,exists,defaultMode}`（任务 2 定义，与任务 10 C# `ToolListItem(Tool, Exists, DefaultMode)` 序列化一致）；桥方法名前后端一致（`tools.*`/`profiles.*`/`settings.*`/`workdirs.*`/`sessions.list`/`terminal.*`/`app.info`/`launch.external`）。

## 合并后跟进项（最终整体审查 2026-08-15）

已随合并处理：规格 §4.5 原生对话框描述回写为应用内弹窗（M3）；csproj 显式 Version 0.1.0（M6）。

遗留跟进（均有 toast/等价兜底，非功能缺失）：
- I1：终端 spawn 失败按规格应为"标签内错误+重试按钮"，当前实现为错误 toast + 手动重点启动（App.tsx handleLaunch/handleNewShell catch）。
- I2：启动校验错误（exe/workdir 不存在）无结构化字段定位与配置项跳转，当前 toast 文本已指明缺什么；LaunchService.Validate 抛 InvalidOperationException 归 internal 码，可改 BridgeException("validation")。
- M1：terminal.exit 事件后端发射、前端零消费者（sessions.changed 等价覆盖）。
- M2：配置损坏恢复为静默重建，规格要求 UI 提示。
- M4：§4.1 的 iconSource/注册表"未识别"工具收录未实现（实现取 §4.2 单 source 字段 + 前端名字 logo）。
- M5：ui/public/icons.svg 死资产可删。
- M7：桥层薄封装方法（createShell/kill/close、tools.list、profiles.delete、workdirs.remove）无 BridgeTests 直接分发测试（Core 层有测）。
- 真机保留项：本机 WebView2 运行时异常，WPF 宿主内真实 ConPTY 冒烟待环境修复后补做（窗口可开、桥有 76 单测覆盖）。

## v0.2 调整（2026-08-16，用户提出）

1. **自定义标题栏**：`WindowStyle="None"` + WindowChrome（CaptionHeight=0，保留 8px 隐形调整边框）；顶栏右侧加最小化/最大化还原/关闭三按钮；顶栏空白区拖拽（`window.beginDrag` → DragMove）与双击切换最大化；新增桥方法 `window.minimize/toggleMaximize/close/beginDrag/getState` + `window.state.changed` 事件（App 层注册）；最大化时 Root.Margin=7 补偿 WindowChrome 溢出。实现中修复了 toggleMaximize 三元表达式两分支同值的笔误（最大化按钮失效的直接原因）。
2. **工作目录选择改系统原生对话框**：新增桥方法 `dialog.selectDirectory`（App 层 WPF `OpenFolderDialog`，支持 initial 定位）；`FolderPickerModal` 组件删除，Mock 桥返回模拟路径。
3. **全局滚动条样式**：webkit + firefox 双轨深色主题滚动条（令牌色，hover 加亮）。
