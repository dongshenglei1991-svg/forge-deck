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
        return [...this.sessions]; // 真实桥每次返回新 JSON 数组；副本保证 React 依赖比较生效
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
