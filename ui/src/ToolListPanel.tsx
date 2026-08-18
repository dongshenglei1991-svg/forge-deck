import { useMemo, useState } from 'react';
import type { ToolListItem } from './types';

const LOGOS: Record<string, string> = {
  'Claude Code': 'C/', 'Codex CLI': 'CX', 'Gemini CLI': 'G', 'Grok Build': 'GB',
  'OpenCode': 'OC', 'GitHub Copilot CLI': 'GH', 'Qwen Code': 'Qw', 'Goose': 'Go',
  'Amp': 'Am', 'Crush': 'Cr', 'Continue CLI': 'Cn', 'Kiro CLI': 'Ki', 'iFlow CLI': 'iF',
  'Aider': 'Ai',
  'Cursor': 'Cu', 'Cursor Agent': 'Cu', 'Windsurf': 'W', 'Trae': 'T', 'Zed': 'Z', 'VS Code': 'VS',
};
const logoFor = (name: string) => LOGOS[name] ?? name.slice(0, 2);

export function ToolListPanel({ tools, scanning, selectedToolId, onSelect, onRescan }: {
  tools: ToolListItem[]; scanning: boolean; selectedToolId: string | null;
  onSelect: (id: string) => void; onRescan: () => void;
}) {
  const [query, setQuery] = useState('');
  const q = query.trim().toLowerCase();
  const filtered = useMemo(() => tools.filter((item) => {
    if (!q) return true;
    return item.tool.name.toLowerCase().includes(q) || item.tool.exePath.toLowerCase().includes(q);
  }), [tools, q]);
  return (
    <section className="panel">
      <div className="panel-head">
        <span className="panel-title">本机工具</span>
        <span className="panel-meta">{scanning ? '正在扫描…' : '自动扫描 · 已完成'}</span>
      </div>
      <div className="list-search">
        <input className="input" placeholder="搜索名称或路径" value={query}
          onChange={(e) => setQuery(e.target.value)} aria-label="搜索本机工具" />
      </div>
      <div className="tool-list">
        {filtered.map((item) => (
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
        {filtered.length === 0 && !scanning &&
          <div className="tool-path" style={{ padding: '14px 10px' }}>
            {tools.length === 0 ? '未识别到已安装工具，试试重新扫描或手动添加。' : '没有匹配的工具。'}
          </div>}
      </div>
      <div className="scan-row">
        <span>{scanning ? '正在检查 PATH 与已知安装位置' : '已扫描已知目录、PATH、注册表、开始菜单'}</span>
        <button className="btn small" onClick={onRescan} disabled={scanning}>重新扫描</button>
      </div>
    </section>
  );
}
