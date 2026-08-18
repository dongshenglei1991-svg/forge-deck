import { useMemo, useState } from 'react';
import type { HiddenTool, ToolListItem } from './types';

export function ToolsView({ tools, hidden, onRescan, onHide, onDelete, onRelocate, onUnhide }: {
  tools: ToolListItem[];
  hidden: HiddenTool[];
  onRescan: () => void;
  onHide: (toolId: string) => void;
  onDelete: (toolId: string) => void;
  onRelocate: (toolId: string) => void;
  onUnhide: (exePath: string) => void;
}) {
  const [query, setQuery] = useState('');
  const [showHidden, setShowHidden] = useState(false);
  const q = query.trim().toLowerCase();
  const filtered = useMemo(() => tools.filter((item) => {
    if (!q) return true;
    return item.tool.name.toLowerCase().includes(q) || item.tool.exePath.toLowerCase().includes(q);
  }), [tools, q]);

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
        <div className="list-search">
          <input className="input" placeholder="搜索名称或路径" value={query}
            onChange={(e) => setQuery(e.target.value)} aria-label="搜索工具" />
        </div>
        <table className="data-table">
          <thead>
            <tr><th>工具</th><th>可执行文件</th><th>来源</th><th>默认方式</th><th>状态</th><th>操作</th></tr>
          </thead>
          <tbody>
            {filtered.map((item) => (
              <tr key={item.tool.id}>
                <td><strong>{item.tool.name}</strong></td>
                <td className="path-cell">{item.tool.exePath}</td>
                <td>{item.tool.source}</td>
                <td>{item.defaultMode === 'embedded' ? '内嵌终端' : '独立窗口'}</td>
                <td className="status-text">{item.exists ? '已安装' : '文件缺失'}</td>
                <td>
                  <div className="table-actions">
                    {!item.exists && (
                      <button className="btn small" type="button" onClick={() => onRelocate(item.tool.id)}>重新定位</button>
                    )}
                    {item.tool.manual
                      ? <button className="btn small" type="button" onClick={() => onDelete(item.tool.id)}>删除</button>
                      : <button className="btn small" type="button" onClick={() => onHide(item.tool.id)}>隐藏</button>}
                  </div>
                </td>
              </tr>
            ))}
            {filtered.length === 0 && (
              <tr><td colSpan={6} className="path-cell">
                {tools.length === 0 ? '尚未识别到工具。' : '没有匹配的工具。'}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
      <div className="hidden-panel">
        <button className="btn small" type="button" onClick={() => setShowHidden((v) => !v)}>
          {showHidden ? '收起已隐藏' : `已隐藏（${hidden.length}）`}
        </button>
        {showHidden && (
          hidden.length === 0
            ? <p className="sub">没有隐藏的工具。</p>
            : <ul className="hidden-list">
              {hidden.map((h) => (
                <li key={h.exePath}>
                  <div>
                    <strong>{h.name}</strong>
                    <div className="path-cell">{h.exePath}</div>
                  </div>
                  <button className="btn small" type="button" onClick={() => onUnhide(h.exePath)}>取消隐藏</button>
                </li>
              ))}
            </ul>
        )}
      </div>
    </>
  );
}
