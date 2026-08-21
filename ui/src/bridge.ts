import type { AppInfo, FsEntry, FsListResult, HiddenTool, LaunchProfile, SettingsInfo, TerminalSessionInfo, ToolListItem } from './types';

function e(name: string, path: string, isDirectory: boolean, extension: string): FsEntry {
  return { name, path, isDirectory, extension };
}

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
    { tool: { id: 't-claude', name: 'Claude Code', type: 'cli', exePath: 'C:\\Users\\dev\\AppData\\Roaming\\npm\\claude.cmd', source: 'npm 全局', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-codex', name: 'Codex CLI', type: 'cli', exePath: 'C:\\Users\\dev\\.local\\bin\\codex.exe', source: '用户目录', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-grok', name: 'Grok Build', type: 'cli', exePath: 'C:\\Users\\dev\\.grok\\bin\\grok.exe', source: '用户目录', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-opencode', name: 'OpenCode', type: 'cli', exePath: 'C:\\Users\\dev\\AppData\\Roaming\\npm\\opencode.cmd', source: 'npm 全局', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-cursor', name: 'Cursor Agent', type: 'cli', exePath: 'C:\\Program Files\\Cursor\\resources\\app\\bin\\cursor-agent.exe', source: '开始菜单', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'external' },
    { tool: { id: 't-aider', name: 'Aider', type: 'cli', exePath: 'C:\\Users\\dev\\AppData\\Local\\Programs\\Python\\Scripts\\aider.exe', source: 'Python Scripts', builtin: true, manual: false, pathPinned: false }, exists: true, defaultMode: 'embedded' },
    { tool: { id: 't-missing', name: '旧版 Aider', type: 'cli', exePath: 'C:\\gone\\aider.exe', source: '用户目录', builtin: true, manual: false, pathPinned: false }, exists: false, defaultMode: 'embedded' },
    { tool: { id: 't-manual', name: '自研脚本', type: 'cli', exePath: 'C:\\Tools\\mine.cmd', source: '手动添加', builtin: false, manual: true, pathPinned: false }, exists: true, defaultMode: 'embedded' },
  ];
  private readonly settings: SettingsInfo = {
    settings: { defaultShell: 'pwsh', autoScanOnStartup: true, extraScanDirs: [], skipExitConfirm: false, preferEmbedded: true, maxWorkdirHistory: 20, closeBehavior: 'ask' },
    commonDirs: [
      { name: '主目录', path: 'C:\\Users\\dev' },
      { name: '桌面', path: 'C:\\Users\\dev\\Desktop' },
      { name: '文档', path: 'C:\\Users\\dev\\Documents' },
      { name: 'C:\\', path: 'C:\\' },
    ],
    userName: 'Dev',
  };
  private readonly profiles: LaunchProfile[] = [];
  private readonly lastProfileByTool = new Map<string, string>();
  private readonly hidden: HiddenTool[] = [];

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

  private visibleTools() {
    const hidden = new Set(this.hidden.map((h) => h.exePath.toLowerCase()));
    return this.tools.filter((t) => !hidden.has(t.tool.exePath.toLowerCase()));
  }

  private profilesFor(toolId: string) {
    return this.profiles
      .filter((p) => p.toolId === toolId)
      .sort((a, b) => {
        const da = a.name.toLowerCase() === '默认' ? 0 : 1;
        const db = b.name.toLowerCase() === '默认' ? 0 : 1;
        return da - db || a.name.localeCompare(b.name, 'zh');
      });
  }

  private uniqueProfileName(toolId: string, stem: string) {
    const names = new Set(this.profiles.filter((p) => p.toolId === toolId).map((p) => p.name.toLowerCase()));
    if (!names.has(stem.toLowerCase())) return stem;
    for (let i = 2; ; i++) {
      const c = `${stem} ${i}`;
      if (!names.has(c.toLowerCase())) return c;
    }
  }

  private getOrCreateProfile(toolId: string) {
    const lastId = this.lastProfileByTool.get(toolId);
    const last = lastId ? this.profiles.find((p) => p.id === lastId && p.toolId === toolId) : undefined;
    if (last) return last;
    const first = this.profilesFor(toolId)[0];
    if (first) {
      this.lastProfileByTool.set(toolId, first.id);
      return first;
    }
    const fresh: LaunchProfile = { id: `p-${++this.seq}`, toolId, name: '默认', args: '', env: {}, workdir: '', openMode: 'embedded', autoRestore: false };
    this.profiles.push(fresh);
    this.lastProfileByTool.set(toolId, fresh.id);
    return fresh;
  }

  private readonly mockTree: Record<string, FsEntry[]> = {
    'C:\\Projects\\atlas-web': [
      e('src', 'C:\\Projects\\atlas-web\\src', true, ''),
      e('node_modules', 'C:\\Projects\\atlas-web\\node_modules', true, ''),
      e('go.mod', 'C:\\Projects\\atlas-web\\go.mod', false, 'mod'),
      e('pom.xml', 'C:\\Projects\\atlas-web\\pom.xml', false, 'xml'),
      e('Program.cs', 'C:\\Projects\\atlas-web\\Program.cs', false, 'cs'),
      e('package.json', 'C:\\Projects\\atlas-web\\package.json', false, 'json'),
      e('README.md', 'C:\\Projects\\atlas-web\\README.md', false, 'md'),
      e('.gitignore', 'C:\\Projects\\atlas-web\\.gitignore', false, ''),
    ],
    'C:\\Projects\\atlas-web\\src': [
      e('App.tsx', 'C:\\Projects\\atlas-web\\src\\App.tsx', false, 'tsx'),
      e('main.ts', 'C:\\Projects\\atlas-web\\src\\main.ts', false, 'ts'),
      e('app.css', 'C:\\Projects\\atlas-web\\src\\app.css', false, 'css'),
      e('Main.java', 'C:\\Projects\\atlas-web\\src\\Main.java', false, 'java'),
      e('app.go', 'C:\\Projects\\atlas-web\\src\\app.go', false, 'go'),
    ],
    'C:\\Projects\\atlas-web\\node_modules': [
      e('index.js', 'C:\\Projects\\atlas-web\\node_modules\\index.js', false, 'js'),
    ],
  };

  private handle(method: string, p: any): any {
    switch (method) {
      case 'app.info':
        return { version: '0.1.0', userName: this.settings.userName, lastScanAt: new Date().toISOString(), lastUsed: { toolId: 't-claude', workdir: 'C:\\Projects\\atlas-web' } } satisfies AppInfo;
      case 'tools.list':
      case 'tools.rescan':
        return this.visibleTools();
      case 'tools.addManual': {
        if (!p.name.trim()) throw new Error('工具名称不能为空');
        const id = `t-manual-${++this.seq}`;
        this.tools.push({ tool: { id, name: p.name, type: 'cli', exePath: p.exePath, source: '手动添加', builtin: false, manual: true, pathPinned: false }, exists: true, defaultMode: 'embedded' });
        return this.visibleTools();
      }
      case 'tools.hide': {
        const item = this.tools.find((t) => t.tool.id === p.toolId);
        if (!item) throw new Error('工具不存在');
        if (item.tool.manual) throw new Error('手动添加的工具请删除，不能隐藏');
        this.hidden.push({ exePath: item.tool.exePath, name: item.tool.name, source: item.tool.source, toolId: item.tool.id });
        return this.visibleTools();
      }
      case 'tools.unhide': {
        const i = this.hidden.findIndex((h) => h.exePath.toLowerCase() === String(p.exePath).toLowerCase());
        if (i >= 0) this.hidden.splice(i, 1);
        return this.visibleTools();
      }
      case 'tools.delete': {
        const idx = this.tools.findIndex((t) => t.tool.id === p.toolId);
        if (idx < 0) throw new Error('工具不存在');
        if (!this.tools[idx].tool.manual) throw new Error('只能删除手动添加的工具');
        this.tools.splice(idx, 1);
        for (let i = this.profiles.length - 1; i >= 0; i--) {
          if (this.profiles[i].toolId === p.toolId) this.profiles.splice(i, 1);
        }
        return this.visibleTools();
      }
      case 'tools.relocate': {
        const item = this.tools.find((t) => t.tool.id === p.toolId);
        if (!item) throw new Error('工具不存在');
        item.tool.exePath = p.exePath;
        item.tool.pathPinned = true;
        item.exists = true;
        return item;
      }
      case 'tools.hidden':
        return [...this.hidden];
      case 'profiles.get':
        return this.getOrCreateProfile(p.toolId);
      case 'profiles.list':
        return this.profilesFor(p.toolId);
      case 'profiles.save': {
        const profile = p.profile as LaunchProfile;
        const i = this.profiles.findIndex((x) => x.id === profile.id);
        if (i >= 0) this.profiles[i] = profile; else this.profiles.push(profile);
        this.lastProfileByTool.set(profile.toolId, profile.id);
        return profile;
      }
      case 'profiles.create': {
        const from = p.fromProfileId ? this.profiles.find((x) => x.id === p.fromProfileId) : undefined;
        const created: LaunchProfile = {
          id: `p-${++this.seq}`,
          toolId: p.toolId,
          name: this.uniqueProfileName(p.toolId, from ? '副本' : '默认'),
          args: from?.args ?? '',
          env: { ...(from?.env ?? {}) },
          workdir: from?.workdir ?? '',
          openMode: from?.openMode ?? 'embedded',
          autoRestore: from?.autoRestore ?? false,
        };
        this.profiles.push(created);
        this.lastProfileByTool.set(p.toolId, created.id);
        return created;
      }
      case 'profiles.rename': {
        const profile = this.profiles.find((x) => x.id === p.id);
        if (!profile) throw new Error('配置不存在');
        const name = String(p.name ?? '').trim();
        if (!name) throw new Error('配置名称不能为空');
        if (this.profiles.some((x) => x.id !== p.id && x.toolId === profile.toolId && x.name.toLowerCase() === name.toLowerCase()))
          throw new Error('该工具已有同名配置');
        profile.name = name;
        return profile;
      }
      case 'profiles.delete': {
        const i = this.profiles.findIndex((x) => x.id === p.id);
        if (i < 0) throw new Error('配置不存在');
        const toolId = this.profiles[i].toolId;
        this.profiles.splice(i, 1);
        let current = this.profilesFor(toolId)[0];
        if (!current) {
          current = { id: `p-${++this.seq}`, toolId, name: '默认', args: '', env: {}, workdir: '', openMode: 'embedded', autoRestore: false };
          this.profiles.push(current);
        }
        this.lastProfileByTool.set(toolId, current.id);
        return current;
      }
      case 'profiles.select': {
        const profile = this.profiles.find((x) => x.id === p.profileId && x.toolId === p.toolId);
        if (!profile) throw new Error('配置不存在');
        this.lastProfileByTool.set(p.toolId, profile.id);
        return profile;
      }
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
      case 'fs.list': {
        const { path } = this.guardFs(p);
        const entries = this.mockTree[path];
        if (!entries) throw new Error('not_found: 目录不存在');
        return { path, entries } satisfies FsListResult;
      }
      case 'fs.open': {
        const { path } = this.guardFs(p);
        const kind = this.mockKind(path);
        if (kind === 'dir') throw new Error('validation: 只能打开文件');
        if (kind === 'missing') throw new Error('not_found: 文件不存在');
        return null;
      }
      case 'fs.openWithSystem': {
        const { path } = this.guardFs(p);
        if (this.mockKind(path) === 'missing') throw new Error('not_found: 路径不存在');
        return null;
      }
      case 'fs.delete': {
        const { path, root } = this.guardFs(p);
        if (path.toLowerCase() === root.toLowerCase()) throw new Error('validation: 不能删除工作目录根');
        if (this.mockKind(path) === 'missing') throw new Error('not_found: 路径不存在');
        const prefix = path + '\\';
        for (const key of Object.keys(this.mockTree)) {
          if (key === path || key.startsWith(prefix)) delete this.mockTree[key];
        }
        const slash = path.lastIndexOf('\\');
        const parent = slash >= 0 ? path.slice(0, slash) : path;
        const list = this.mockTree[parent];
        if (list) {
          const i = list.findIndex((e) => e.path.toLowerCase() === path.toLowerCase());
          if (i >= 0) list.splice(i, 1);
        }
        return null;
      }
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
      case 'window.minimize':
      case 'window.toggleMaximize':
      case 'window.beginDrag':
      case 'window.beginResize':
        return null; // 浏览器预览无窗口控制，静默
      case 'window.close': {
        const behavior = this.settings.settings.closeBehavior;
        if (behavior === 'ask') this.emit('window.close.prompt', {});
        else if (behavior === 'minimizeToTray') this.emit('window.tray.mocked', {});
        return null;
      }
      case 'window.hideToTray':
        this.emit('window.tray.mocked', {});
        return null;
      case 'window.exit': {
        const running = this.sessions.filter((s) => s.running).length;
        if (running > 0 && !this.settings.settings.skipExitConfirm)
          this.emit('window.exit.confirm', { running });
        return null;
      }
      case 'window.confirmExit':
        return null;
      case 'window.getState':
        return { maximized: false };
      case 'dialog.selectDirectory':
        return { path: 'C:\\Users\\dev\\Documents\\SelectedFolder' };
      case 'dialog.selectFile':
        return { path: 'C:\\Users\\dev\\AppData\\Local\\Programs\\Python\\Scripts\\aider.exe' };
      default:
        throw new Error(`未知方法：${method}`);
    }
  }

  private guardFs(p: any): { path: string; root: string } {
    const root = String(p?.root ?? '');
    const path = String(p?.path ?? '');
    if (!root.trim() || !path.trim()) throw new Error('validation: 路径不能为空');
    const prefix = root.endsWith('\\') ? root : root + '\\';
    const under = path.toLowerCase() === root.toLowerCase()
      || path.toLowerCase().startsWith(prefix.toLowerCase());
    if (!under) throw new Error('validation: 路径超出工作目录');
    return { path, root };
  }

  private mockKind(path: string): 'file' | 'dir' | 'missing' {
    const p = path.toLowerCase();
    for (const key of Object.keys(this.mockTree)) {
      if (key.toLowerCase() === p) return 'dir';
    }
    for (const entries of Object.values(this.mockTree)) {
      const hit = entries.find((e) => e.path.toLowerCase() === p);
      if (hit) return hit.isDirectory ? 'dir' : 'file';
    }
    return 'missing';
  }

  private mockOutput(id: string, title: string) {
    const lines = [`${title} · Mock 终端（浏览器预览）\r\n`, '在 WebView2 宿主中运行时连接真实 ConPTY。\r\n'];
    // 首块 0ms 立即发射：模拟真实 ConPTY 在 createShell 响应与 sessions 列表往返之前就开始输出（考验早到缓冲）
    lines.forEach((chunk, i) => setTimeout(() => this.emit('terminal.data', { sessionId: id, chunk }), i === 0 ? 0 : 300));
  }
}

export const bridge: Bridge = window.chrome?.webview ? new WebViewBridge() : new MockBridge();
