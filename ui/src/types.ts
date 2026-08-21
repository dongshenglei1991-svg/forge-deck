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
  pathPinned: boolean;
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
  closeBehavior: 'ask' | 'exit' | 'minimizeToTray';
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

export interface HiddenTool {
  exePath: string;
  name: string;
  source: string;
  toolId: string | null;
}

export interface AppInfo {
  version: string;
  userName: string;
  lastScanAt: string | null;
  lastUsed: { toolId: string; workdir: string } | null;
}
