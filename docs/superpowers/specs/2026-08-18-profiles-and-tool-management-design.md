# ForgeDeck 设计文档 — 多 Profile 与工具管理

- 日期：2026-08-18
- 状态：已批准（方案 1）
- 前置：`docs/superpowers/specs/2026-08-14-launcher-design.md`

## 1. 背景

MVP 每个工具只保留一套启动配置（`profiles.save` 按 `toolId` 覆盖），工具库是只读表：不能搜、不能删手加项、不能藏自动识别项、文件缺失不能改路径。本规格补完这些能力，不改变「扫描 + 启动」主线。

已确认决策：

- 手加工具可永久删除；自动识别的只能隐藏。
- 隐藏按可执行路径记名单，重扫不会把同一路径加回可见列表。
- 文件缺失时可重新定位；定位后路径钉住，重扫不覆盖。
- 配置用配置面板顶部下拉切换；新建 = 复制当前；每个工具至少留一套，删光自动补「默认」。
- 快速启动页只做搜索过滤；管理动作放在工具库。

## 2. 数据模型

`AppConfig` 新增（缺省为空，旧 `config.json` 无需迁移，`version` 保持 1）：

```json
{
  "hiddenExePaths": ["C:\\...\\tool.exe"],
  "lastProfileByTool": { "<toolId>": "<profileId>" }
}
```

- `hiddenExePaths`：`Path.GetFullPath` 规范化，比较用 `OrdinalIgnoreCase`。只通过隐藏 / 取消隐藏改写。
- `lastProfileByTool`：每个工具上次选中的配置。切换、保存、启动时更新。

`ToolInfo` 新增 `pathPinned`（默认 `false`）。仅「重新定位」置 `true`。钉住后重扫保留该条目且不改 `exePath`；若扫描命中同一路径，只刷新名称 / 类型 / 来源 / `builtin`。

`LaunchProfile` 字段不变。`profiles.save` 只按 `id` 覆盖，不再按 `toolId` 清掉其它配置。

## 3. 重扫合并

在现有「按路径复用 Id、手加永远保留」上增加：

1. 扫描命中若路径在 `hiddenExePaths`：不进入可见列表；库中已有同路径条目则保留（取消隐藏时名称还在）。名单中有路径但库中无条目时，不新建（`tools.hidden` 只显示路径）。
2. `pathPinned` 条目与手加同样保留，路径不被扫到的其它位置覆盖。扫描在另一路径发现同一已知工具时，作为新条目加入（用户可再隐藏）。
3. 未隐藏、未钉住、又没扫到的自动识别条目，仍淘汰。

## 4. 桥方法

### 4.1 行为变更

| 方法 | 变化 |
|---|---|
| `tools.list` / `tools.rescan` | 只返回未隐藏工具；`rescan` 按第 3 节合并 |
| `profiles.get` | 优先返回 `lastProfileByTool` 仍存在的那套；否则该工具第一套（「默认」优先）；没有则当场创建并持久化「默认」，记为当前 |
| `profiles.save` | 只按 `id` 覆盖；并把该 `toolId` 的当前配置写成刚保存的这套 |

### 4.2 新增（Core）

| 方法 | 参数 | 结果 |
|---|---|---|
| `profiles.list` | `{ toolId }` | 该工具全部配置。「默认」（名称 OrdinalIgnoreCase）始终在最前，其余按名称排序 |
| `profiles.create` | `{ toolId, fromProfileId? }` | 复制指定配置，名称「副本」/「副本 2」…；无源则建空白「默认」结构。成功后记为当前并返回新对象 |
| `profiles.rename` | `{ id, name }` | trim 后非空；同一工具下名称 OrdinalIgnoreCase 不重复 |
| `profiles.delete` | `{ id }` | 删后若该工具一套不剩，自动补空白「默认」并选中；若删的是当前套，改选剩余第一套 |
| `profiles.select` | `{ toolId, profileId }` | 只改 `lastProfileByTool` |
| `tools.hide` | `{ toolId }` | 仅非手加；路径写入 `hiddenExePaths`；返回可见列表 |
| `tools.unhide` | `{ exePath }` | 从名单去掉；返回可见列表 |
| `tools.delete` | `{ toolId }` | 仅手加；删除工具及其全部配置、`lastProfileByTool` 项；若 `lastUsed.toolId` 指向它则清空 `lastUsed` |
| `tools.relocate` | `{ toolId, exePath }` | 新文件必须存在；新路径不能被其它工具占用；写回路径并 `pathPinned=true` |
| `tools.hidden` | 无 | `{ exePath, name, source, toolId }[]`。库内有快照则带名称，否则 `name` 为路径、`toolId` 为 null |

### 4.3 新增（App 层）

| 方法 | 说明 |
|---|---|
| `dialog.selectFile` | 系统文件对话框，滤 `*.exe;*.cmd;*.bat;*.ps1`。取消返回 `null` |

`MockBridge` 必须同步实现上述方法。

## 5. 错误处理

一律 `{ error: { code, message } }`。

| 场景 | code | 前端 |
|---|---|---|
| 手加才能删 / 扫描才能藏 | `validation` | Toast |
| 重新定位：文件不存在、路径已被占用 | `validation` | Toast；不改原路径 |
| 配置名空或重名 | `validation` | 下拉就地提示 |
| 工具 / 配置不存在 | `not_found` | Toast |
| 选文件取消 | 无错误 | `null`，UI 不动 |

交互约定：

- 切换配置前先 `profiles.save` 当前草稿；保存失败则不切换。
- 启动仍先保存当前配置，再用选中的那套启动。
- 隐藏 / 删除当前选中工具后，选可见列表第一项；列表空则清空配置面板。
- 重新定位只在「文件缺失」时出现，走 `dialog.selectFile`。
- 删除手加工具前用浏览器 `confirm` 确认。

## 6. 界面

沿用现有令牌与组件，不新开视图。

**快速启动 / 本机工具：** 列表顶部搜索框，按名称或路径（不区分大小写）过滤。无管理按钮。

**配置面板：** 标题行增加配置下拉：列出该工具全部配置；可切换、新建（复制当前）、重命名（行内）、删除。每个工具至少一套。

**工具库：** 表格增加搜索；操作列：手加显示「删除」，扫描显示「隐藏」，`exists=false` 另显示「重新定位」。表下可展开「已隐藏」列表，每项可「取消隐藏」。

## 7. 测试

`BridgeTests` 覆盖：多配置保存互不覆盖；`list/create/rename/delete/select`；删最后一套补「默认」；隐藏后 list/rescan 不可见且同路径不回潮；取消隐藏后可见；手加删除清配置；扫描项删除被拒；重新定位钉住路径后重扫不改路径；路径冲突。

`ConfigStoreTests` 覆盖 `hiddenExePaths` / `lastProfileByTool` / `pathPinned` 读写往返。

前端无单测：`npm run build` + `npm run lint` 通过；Mock 下可点通搜索、下拉、隐藏/删除/重新定位。
