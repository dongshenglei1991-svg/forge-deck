import type { ToolListItem } from './types';

export function ToolsView({ tools, onRescan }: { tools: ToolListItem[]; onRescan: () => void }) {
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
        <table className="data-table">
          <thead>
            <tr><th>工具</th><th>可执行文件</th><th>来源</th><th>默认方式</th><th>状态</th></tr>
          </thead>
          <tbody>
            {tools.map((item) => (
              <tr key={item.tool.id}>
                <td><strong>{item.tool.name}</strong></td>
                <td className="path-cell">{item.tool.exePath}</td>
                <td>{item.tool.source}</td>
                <td>{item.defaultMode === 'embedded' ? '内嵌终端' : '独立窗口'}</td>
                <td className="status-text">{item.exists ? '已安装' : '文件缺失'}</td>
              </tr>
            ))}
            {tools.length === 0 && <tr><td colSpan={5} className="path-cell">尚未识别到工具。</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  );
}
