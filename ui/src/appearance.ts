import type { AccentColor, ColorMode, ResolvedColorMode } from './types';

const MODES = new Set<ColorMode>(['dark', 'light', 'system']);
const ACCENT_IDS = new Set<AccentColor>(['teal', 'blue', 'violet', 'amber', 'rose']);

export const COLOR_MODES: { value: ColorMode; label: string }[] = [
  { value: 'dark', label: '深色' },
  { value: 'light', label: '浅色' },
  { value: 'system', label: '跟随系统' },
];

export const ACCENTS: { id: AccentColor; label: string; hue: number }[] = [
  { id: 'teal', label: '青绿', hue: 155 },
  { id: 'blue', label: '蓝', hue: 250 },
  { id: 'violet', label: '紫', hue: 300 },
  { id: 'amber', label: '琥珀', hue: 75 },
  { id: 'rose', label: '玫红', hue: 15 },
];

export function normalizeColorMode(value: unknown): ColorMode {
  return typeof value === 'string' && MODES.has(value as ColorMode) ? value as ColorMode : 'dark';
}

export function normalizeAccentColor(value: unknown): AccentColor {
  return typeof value === 'string' && ACCENT_IDS.has(value as AccentColor) ? value as AccentColor : 'teal';
}

export function resolveColorMode(mode: ColorMode): ResolvedColorMode {
  if (mode === 'light') return 'light';
  if (mode === 'dark') return 'dark';
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

const THEME_KEY = 'forgedeck-theme';
const ACCENT_KEY = 'forgedeck-accent';

/** 把外观写到 <html> 上，CSS 令牌与终端/编辑器都读这些属性。system 解析为当前系统浅/深色。 */
export function applyAppearance(mode: ColorMode, accent: AccentColor): ResolvedColorMode {
  const resolved = resolveColorMode(mode);
  const html = document.documentElement;
  html.dataset.theme = resolved;
  html.dataset.accent = accent;
  html.style.colorScheme = resolved;
  const meta = document.querySelector('meta[name="color-scheme"]');
  if (meta) meta.setAttribute('content', resolved);
  try {
    localStorage.setItem(THEME_KEY, mode);
    localStorage.setItem(ACCENT_KEY, accent);
  } catch { /* 无痕/禁用存储时仍可当场换肤 */ }
  return resolved;
}

export function monacoThemeName(mode: ResolvedColorMode): 'forgedeck-dark' | 'forgedeck-light' {
  return mode === 'light' ? 'forgedeck-light' : 'forgedeck-dark';
}

/** 把当前 CSS 令牌读成 xterm 能吃的颜色（canvas 会把 oklch 解析成 rgb）。 */
export function readXtermTheme(): {
  background: string; foreground: string; cursor: string; cursorAccent: string; selectionBackground: string;
} {
  const s = getComputedStyle(document.documentElement);
  const token = (name: string) => s.getPropertyValue(name).trim();
  const bg = token('--term-bg') || token('--bg');
  const fg = token('--term-fg') || token('--muted');
  const accent = token('--accent');
  return {
    background: bg,
    foreground: fg,
    cursor: accent,
    cursorAccent: bg,
    selectionBackground: cssToRgba(accent, 0.28) ?? 'rgba(140, 255, 190, 0.25)',
  };
}

function cssToRgba(css: string, alpha: number): string | null {
  if (!css) return null;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 1;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.fillStyle = '#000';
  ctx.fillStyle = css;
  const parsed = ctx.fillStyle;
  const hex = /^#([0-9a-f]{6})$/i.exec(parsed);
  if (hex) {
    const n = parseInt(hex[1], 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
  }
  const rgb = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/i.exec(parsed);
  if (!rgb) return null;
  return `rgba(${rgb[1]}, ${rgb[2]}, ${rgb[3]}, ${alpha})`;
}
