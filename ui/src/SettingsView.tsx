import { useEffect, useState, type CSSProperties } from 'react';
import { ACCENTS, COLOR_MODES, normalizeAccentColor, normalizeColorMode } from './appearance';
import { MenuSelect } from './MenuSelect';
import { Switch } from './Switch';
import type { AccentColor, AppSettings, ColorMode, SettingsInfo } from './types';

export function SettingsView({ info, onSave, onAppearance }: {
  info: SettingsInfo;
  onSave: (s: AppSettings) => void;
  onAppearance: (colorMode: ColorMode, accentColor: AccentColor) => void;
}) {
  const [extraDirs, setExtraDirs] = useState(info.settings.extraScanDirs.join('\n'));
  const [autoScan, setAutoScan] = useState(info.settings.autoScanOnStartup);
  const [shell, setShell] = useState<string>(info.settings.defaultShell);
  const [skipExitConfirm, setSkipExitConfirm] = useState(info.settings.skipExitConfirm);
  const [preferEmbedded, setPreferEmbedded] = useState(info.settings.preferEmbedded);
  const [closeBehavior, setCloseBehavior] = useState(info.settings.closeBehavior);
  const [colorMode, setColorMode] = useState<ColorMode>(() => normalizeColorMode(info.settings.colorMode));
  const [accentColor, setAccentColor] = useState<AccentColor>(() => normalizeAccentColor(info.settings.accentColor));

  const dirsText = info.settings.extraScanDirs.join('\n');
  useEffect(() => { setExtraDirs(dirsText); }, [dirsText]);
  useEffect(() => { setAutoScan(info.settings.autoScanOnStartup); }, [info.settings.autoScanOnStartup]);
  useEffect(() => { setShell(info.settings.defaultShell); }, [info.settings.defaultShell]);
  useEffect(() => { setSkipExitConfirm(info.settings.skipExitConfirm); }, [info.settings.skipExitConfirm]);
  useEffect(() => { setPreferEmbedded(info.settings.preferEmbedded); }, [info.settings.preferEmbedded]);
  useEffect(() => { setCloseBehavior(info.settings.closeBehavior); }, [info.settings.closeBehavior]);
  useEffect(() => { setColorMode(normalizeColorMode(info.settings.colorMode)); }, [info.settings.colorMode]);
  useEffect(() => { setAccentColor(normalizeAccentColor(info.settings.accentColor)); }, [info.settings.accentColor]);

  // 载荷侧校验：非法 shell（如手输 bash）回退 pwsh；输入框保留用户原文，不就地改写
  const shellValue = (['pwsh', 'powershell', 'cmd'].includes(shell.trim()) ? shell.trim() : 'pwsh') as AppSettings['defaultShell'];
  const save = () => onSave({
    ...info.settings,
    defaultShell: shellValue,
    autoScanOnStartup: autoScan,
    extraScanDirs: extraDirs.split(/\r?\n/).map((s) => s.trim()).filter(Boolean),
    skipExitConfirm,
    preferEmbedded,
    closeBehavior,
    colorMode,
    accentColor,
  });

  const changeMode = (next: ColorMode) => {
    setColorMode(next);
    onAppearance(next, accentColor);
  };
  const changeAccent = (next: AccentColor) => {
    setAccentColor(next);
    onAppearance(colorMode, next);
  };

  return (
    <>
      <div className="main-head">
        <div>
          <p className="eyebrow">SYSTEM PREFERENCES / 04</p>
          <h1 className="title">设置</h1>
          <p className="sub">调整外观、扫描范围、终端行为和启动器偏好。</p>
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
        <article className="setting-card span-2">
          <h2>外观</h2>
          <p>颜色模式可跟随 Windows 浅色 / 深色。更改立即生效并记住。</p>
          <div className="field appearance-mode">
            <label>颜色模式</label>
            <MenuSelect ariaLabel="颜色模式" value={colorMode} options={COLOR_MODES}
              onChange={(v) => changeMode(normalizeColorMode(v))} />
          </div>
          <div className="swatch-row" role="radiogroup" aria-label="主题色">
            {ACCENTS.map((a) => (
              <button key={a.id} type="button" className="swatch"
                role="radio" aria-checked={accentColor === a.id} aria-label={a.label}
                style={{ '--swatch-hue': String(a.hue) } as CSSProperties}
                title={a.label}
                onClick={() => changeAccent(a.id)} />
            ))}
          </div>
          <p className="swatch-caption">{ACCENTS.find((a) => a.id === accentColor)?.label}</p>
        </article>
        <article className="setting-card span-2">
          <h2>关闭行为</h2>
          <p>点击关闭按钮、按 Alt+F4 或从任务栏关闭窗口时。</p>
          <div className="radio-list" role="radiogroup" aria-label="关闭行为">
            {([
              ['ask', '每次询问（关闭或最小化到托盘）'],
              ['exit', '直接退出'],
              ['minimizeToTray', '最小化到托盘'],
            ] as const).map(([value, label]) => (
              <label key={value} className="radio-row">
                <input type="radio" name="closeBehavior" value={value}
                  checked={closeBehavior === value}
                  onChange={() => setCloseBehavior(value)} />
                {label}
              </label>
            ))}
          </div>
        </article>
      </div>
      <div className="setting-actions">
        <button className="btn primary" onClick={save}>保存设置</button>
      </div>
    </>
  );
}
