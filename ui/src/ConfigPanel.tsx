import { useEffect, useRef, useState } from 'react';
import { Switch } from './Switch';
import { WorkdirControl } from './WorkdirControl';
import { parseEnvText, stringifyEnv } from './lib/env';
import type { LaunchProfile, OpenMode, ToolListItem } from './types';

// 与后端 KnownTools.ResumeArgs 对应：有恢复参数的 CLI 显示「自动恢复会话」开关
const RESUMABLE = new Set([
  'Claude Code', 'Grok Build', 'OpenCode', 'GitHub Copilot CLI',
  'Qwen Code', 'Continue CLI', 'Gemini CLI',
]);

export function ConfigPanel({ tool, profile, workdirs, onSave, onLaunch, onBrowse }: {
  tool: ToolListItem; profile: LaunchProfile; workdirs: string[];
  onSave: (p: LaunchProfile) => void | Promise<void>; onLaunch: (p: LaunchProfile) => void;
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
  // 连续保存的 flash 令牌：只有最新一次保存的定时器可以熄灭提示，避免早先的 setTimeout 提前关掉后一次的"已保存"
  const flashSeq = useRef(0);
  const save = async () => {
    try {
      await onSave(current());
      const seq = ++flashSeq.current;
      setSavedFlash(true);
      setTimeout(() => { if (flashSeq.current === seq) setSavedFlash(false); }, 1400);
    } catch {
      // 保存失败不闪成功；错误 toast 由 App 层 handleSaveProfile 弹出
    }
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
