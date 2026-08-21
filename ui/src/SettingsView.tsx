import { useEffect, useState } from 'react';
import { Switch } from './Switch';
import type { AppSettings, SettingsInfo } from './types';

export function SettingsView({ info, onSave }: { info: SettingsInfo; onSave: (s: AppSettings) => void }) {
  const [extraDirs, setExtraDirs] = useState(info.settings.extraScanDirs.join('\n'));
  const [autoScan, setAutoScan] = useState(info.settings.autoScanOnStartup);
  const [shell, setShell] = useState<string>(info.settings.defaultShell);
  const [skipExitConfirm, setSkipExitConfirm] = useState(info.settings.skipExitConfirm);
  const [preferEmbedded, setPreferEmbedded] = useState(info.settings.preferEmbedded);
  const [closeBehavior, setCloseBehavior] = useState(info.settings.closeBehavior);

  useEffect(() => {
    setExtraDirs(info.settings.extraScanDirs.join('\n'));
    setAutoScan(info.settings.autoScanOnStartup);
    setShell(info.settings.defaultShell);
    setSkipExitConfirm(info.settings.skipExitConfirm);
    setPreferEmbedded(info.settings.preferEmbedded);
    setCloseBehavior(info.settings.closeBehavior);
  }, [info]);

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
