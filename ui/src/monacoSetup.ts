// Monaco 按需装配：核心 API + 全部编辑功能（查找/折叠/括号匹配…）+ 常用基础语言（Monarch 主线程高亮）+ JSON 富语言。
// 刻意不 import 包根入口——它会带上全部语言定义与 LSP 客户端，体积翻倍，只读查看器用不上。
// 注意：monaco-editor 0.56 的 exports 把 "./*.js" 映射到 "./esm/vs/*.js"，深路径必须写 monaco-editor/editor/... 形式。
import * as api from 'monaco-editor/editor/editor.api.js';
import 'monaco-editor/features/register.all.js';
import { jsonDefaults } from 'monaco-editor/languages/features/json/register.js';
import 'monaco-editor/languages/definitions/bat/register.js';
import 'monaco-editor/languages/definitions/cpp/register.js';
import 'monaco-editor/languages/definitions/csharp/register.js';
import 'monaco-editor/languages/definitions/css/register.js';
import 'monaco-editor/languages/definitions/dockerfile/register.js';
import 'monaco-editor/languages/definitions/go/register.js';
import 'monaco-editor/languages/definitions/graphql/register.js';
import 'monaco-editor/languages/definitions/html/register.js';
import 'monaco-editor/languages/definitions/ini/register.js';
import 'monaco-editor/languages/definitions/java/register.js';
import 'monaco-editor/languages/definitions/javascript/register.js';
import 'monaco-editor/languages/definitions/less/register.js';
import 'monaco-editor/languages/definitions/lua/register.js';
import 'monaco-editor/languages/definitions/markdown/register.js';
import 'monaco-editor/languages/definitions/mdx/register.js';
import 'monaco-editor/languages/definitions/php/register.js';
import 'monaco-editor/languages/definitions/powershell/register.js';
import 'monaco-editor/languages/definitions/python/register.js';
import 'monaco-editor/languages/definitions/ruby/register.js';
import 'monaco-editor/languages/definitions/rust/register.js';
import 'monaco-editor/languages/definitions/scss/register.js';
import 'monaco-editor/languages/definitions/shell/register.js';
import 'monaco-editor/languages/definitions/sql/register.js';
import 'monaco-editor/languages/definitions/typescript/register.js';
import 'monaco-editor/languages/definitions/xml/register.js';
import 'monaco-editor/languages/definitions/yaml/register.js';
import EditorWorker from 'monaco-editor/editor/editor.worker.js?worker&inline';
import JsonWorker from 'monaco-editor/language/json/json.worker.js?worker&inline';

export * as monaco from 'monaco-editor/editor/editor.api.js';

// 语言 worker 用 ?worker&inline 内联成 blob：发布模式 WebView2 以 file:// 直载 wwwroot，
// 外置 worker 脚本会被 file:// 同源策略拦下，blob worker 不受影响。
(self as unknown as { MonacoEnvironment: unknown }).MonacoEnvironment = {
  getWorker(_workerId: string, label: string): Worker {
    return label === 'json' ? new JsonWorker() : new EditorWorker();
  },
};

// 只读查看器不需要 JSON 校验的 squiggle 噪音（setDiagnosticsOptions 是整体替换，先展开保留其余默认值）
jsonDefaults.setDiagnosticsOptions({ ...jsonDefaults.diagnosticsOptions, validate: false });

// 与 app.css 深色风格一致的编辑器主题。oklch 换算取 TerminalPanel 注释里的十六进制近似值：
// 背景 oklch(13% .02 170) ≈ #0d1211、前景 oklch(78% .02 170) ≈ #b8c4bf、强调 ≈ #8fe3b0。
api.editor.defineTheme('forgedeck-dark', {
  base: 'vs-dark',
  inherit: true,
  rules: [
    { token: '', foreground: 'b8c4bf' },
    { token: 'comment', foreground: '5b6c64', fontStyle: 'italic' },
    { token: 'keyword', foreground: '8fe3b0' },
    { token: 'string', foreground: 'd8c58c' },
    { token: 'string.key', foreground: 'd8c58c' },
    { token: 'number', foreground: 'e0b184' },
    { token: 'regexp', foreground: 'd8a878' },
    { token: 'type', foreground: 'a3d9c3' },
    { token: 'type.identifier', foreground: 'a3d9c3' },
    { token: 'constant', foreground: 'e0b184' },
    { token: 'delimiter', foreground: '7d918a' },
    { token: 'tag', foreground: '8fe3b0' },
    { token: 'attribute.name', foreground: 'd8c58c' },
    { token: 'attribute.value', foreground: 'b8c4bf' },
    { token: 'variable.predefined', foreground: 'a3d9c3' },
    { token: 'metatag', foreground: '7d918a' },
    { token: 'key', foreground: 'd8c58c' },
  ],
  colors: {
    'editor.background': '#0d1211',
    'editor.foreground': '#b8c4bf',
    'editorGutter.background': '#0d1211',
    'editorLineNumber.foreground': '#55665f',
    'editorLineNumber.activeForeground': '#8fe3b0',
    'editorCursor.foreground': '#8fe3b0',
    'editor.selectionBackground': '#8cffbe40',
    'editor.inactiveSelectionBackground': '#8cffbe22',
    'editor.lineHighlightBackground': '#ffffff07',
    'editor.lineHighlightBorder': '#00000000',
    'editorIndentGuide.background1': '#ffffff14',
    'editorIndentGuide.activeBackground1': '#ffffff29',
    'editorWidget.background': '#15201b',
    'editorWidget.border': '#263831',
    'editorOverviewRuler.border': '#00000000',
    'editor.findMatchBackground': '#8fe3b04d',
    'editor.findMatchHighlightBackground': '#8fe3b033',
    'editorBracketMatch.background': '#8fe3b026',
    'editorBracketMatch.border': '#8fe3b066',
    'scrollbarSlider.background': '#ffffff1c',
    'scrollbarSlider.hoverBackground': '#ffffff30',
    'scrollbarSlider.activeBackground': '#8fe3b033',
  },
});

const LANG_BY_EXT: Record<string, string> = {
  ts: 'typescript', tsx: 'typescript', mts: 'typescript', cts: 'typescript',
  js: 'javascript', jsx: 'javascript', mjs: 'javascript', cjs: 'javascript',
  json: 'json',
  css: 'css', scss: 'scss', less: 'less',
  html: 'html', htm: 'html', xhtml: 'html',
  md: 'markdown', markdown: 'markdown', mdx: 'mdx',
  py: 'python', pyw: 'python', pyi: 'python',
  cs: 'csharp',
  c: 'cpp', h: 'cpp', cpp: 'cpp', cc: 'cpp', cxx: 'cpp', hpp: 'cpp', hh: 'cpp', hxx: 'cpp',
  java: 'java', go: 'go', rs: 'rust', rb: 'ruby', php: 'php', lua: 'lua',
  xml: 'xml', xaml: 'xml', svg: 'xml', csproj: 'xml', props: 'xml', targets: 'xml', resx: 'xml', plist: 'xml', config: 'xml',
  yml: 'yaml', yaml: 'yaml',
  ini: 'ini', cfg: 'ini', conf: 'ini', env: 'ini', properties: 'ini', toml: 'ini', editorconfig: 'ini',
  sh: 'shell', bash: 'shell', zsh: 'shell',
  ps1: 'powershell', psm1: 'powershell', psd1: 'powershell',
  bat: 'bat', cmd: 'bat',
  sql: 'sql', graphql: 'graphql', gql: 'graphql',
};

/** 路径 → Monaco 语言 id；无扩展名 / 未知扩展名回落 plaintext（.log 等纯文本不加高亮） */
export function languageForPath(path: string): string {
  const name = path.slice(Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/')) + 1).toLowerCase();
  if (name === 'dockerfile' || name.startsWith('dockerfile.')) return 'dockerfile';
  const dot = name.lastIndexOf('.');
  if (dot <= 0) return 'plaintext'; // 无扩展名，或 .gitignore 这类点开头文件
  return LANG_BY_EXT[name.slice(dot + 1)] ?? 'plaintext';
}
