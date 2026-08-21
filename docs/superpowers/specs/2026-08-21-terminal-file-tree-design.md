# ForgeDeck 设计文档 — 终端会话工作目录文件树

- 日期：2026-08-21
- 状态：待用户审查
- 前置：`docs/superpowers/specs/2026-08-14-launcher-design.md`
- 方案：A（按层懒加载 `fs.list` + 会话页图标栏 + 左侧文件树）

## 1. 背景

内嵌终端按 Profile 的工作目录拉起，但会话页只有 xterm，看不到当前仓库里有哪些目录和文件。本规格在「终端会话」视图增加工作目录文件树，风格对齐现有深色 oklch 界面，并为常见代码文件显示不同类型图标。

已确认决策：

- 树在终端左侧；进入「终端会话」时左侧导航收成 64px 图标栏（复用现有窄屏样式），文件树占回腾出的宽度。其它视图导航保持 224px，不出现文件树。
- 图标用字母色块（对齐工具列表的 `tool-logo`），目录用绿色线描文件夹。
- 按层懒加载：每次只列一层；`node_modules` / `.git` 等大目录在树上可见，默认折叠，点开才读子项。
- 点文件无动作；目录可展开/折叠。
- 树绑定该会话**启动时**的 `workdir`，不跟随终端内 `cd`。
- 不做磁盘监视、打开/复制文件、拖拽改树宽、Git 状态点。

## 2. 目标与非目标

### 目标

1. 进入「终端会话」后，左侧展示当前激活会话工作目录的文件树，右侧仍是标签 + xterm。
2. 树跟随激活标签：切到另一会话则换成该会话的启动 `workdir`；同一会话内 `cd` 不换根。
3. 第一层随会话出现即加载；子目录展开时再请求。
4. 常见代码文件（含 Java / C# / Go）显示不同字母色块。
5. 纯浏览器 `npm run dev` 下 MockBridge 提供可展开的样例树，行为与真桥一致。

### 非目标

- 跟踪 shell 当前目录（`cd`）。
- `FileSystemWatcher` 或其它实时同步。
- 点击打开文件、复制路径、右键菜单、拖拽、重命名、删除。
- 拖拽调整树宽、搜索过滤、多根工作区、Git 装饰。
- 默认隐藏某些目录（大目录只是不预读，不是藏起来）。

## 3. 界面

### 3.1 会话页布局

`view === 'sessions'` 时根节点加现有 `term-stage`，并收窄导航：

- `--rail: 64px`，隐藏品牌文字、`nav-label`、导航按钮内的 `<span>`、`rail-foot` 文字（与 `@media (max-width: 920px)` 同一套规则，提到 `.app.term-stage`，不改 920px 断点本身）。
- 图标仍可切换四个视图。切走会话页后导航恢复 224px。

主区（`view-panel[data-view-panel="sessions"]`）改为两列：

| 列 | 宽度 | 内容 |
|---|---|---|
| 文件树 | 固定 220px | `FileTreePanel` |
| 终端 | `1fr` | 现有标签栏 + xterm / 空状态 |

无会话时：树区域显示「还没有会话」类空文案（不发 `fs.list`）；右侧保持现有空状态与「新建空白会话」。

### 3.2 文件树

顶栏：左侧 `工作区`（小号等宽大写，对齐 `panel-meta` / `nav-label`），右侧当前 `workdir` 的文件夹名（不是全路径），再加刷新图标按钮。

- 根不画成可折叠的 `D:\...` 长路径，第一层条目直接列在顶栏下。
- 目录行：chevron + 绿色线描文件夹图标 + 名称。未展开 chevron 朝右，展开朝下。
- 文件行：字母色块 + 名称。点击文件无动作（无选中态、无手型强制）；目录点击 = 展开/折叠。
- 缩进每层 12px。溢出横向省略，区域自身滚动。
- 行 hover 用 `--fg-soft`，与工具列表一致。

### 3.3 工作目录何时换根

会话的 `TerminalSessionInfo.workdir` 在 `terminal.create` / `terminal.createShell` 时写入，之后只读：

- 工具内嵌启动：`LaunchService.ResolveWorkdir(profile)`（空则用户主目录）。
- 空白会话：始终用户主目录。
- 终端内 `cd`、进程退出：均不改 `workdir`。

文件树换根仅当激活会话变了且其 `workdir` 与当前树的 root 不同（含新开会话成为激活标签）。刷新只重读该启动目录，不读 shell cwd。

## 4. 桥：`fs.list`

加方法动三处：`ForgeDeckBridge.RegisterMethods()`、`ui/src/bridge.ts` 的 `MockBridge.handle()`、`ui/src/types.ts`。App 层窗口方法不涉及。

### 4.1 请求 / 响应

```json
{ "id": 1, "method": "fs.list", "params": { "path": "D:\\work\\atlas-web\\src", "root": "D:\\work\\atlas-web" } }
```

```json
{
  "id": 1,
  "result": {
    "path": "D:\\work\\atlas-web\\src",
    "entries": [
      { "name": "App.tsx", "path": "D:\\work\\atlas-web\\src\\App.tsx", "isDirectory": false, "extension": "tsx" }
    ]
  }
}
```

| 字段 | 规则 |
|---|---|
| `root` | 激活会话 `workdir` 规范化全路径，不能为空 |
| `path` | 要列的目录；必须是 `root` 或 `root` 下的子孙（`OrdinalIgnoreCase`） |
| `entries[].path` | `Path.GetFullPath` 规范化 |
| `entries[].extension` | 文件：扩展名小写且不含点（`tsx`、`cs`）；目录或无扩展名为 `""` |
| 范围 | **只返回这一层**，不含 `.` / `..`，不跟随递归 |
| 排序 | 目录在前、文件在后，组内 `string.Compare(name, OrdinalIgnoreCase)` |
| 点文件 | 包含（`.gitignore`、`.env`） |

`path` / `root` 先 `GetFullPath` 再比较。判断子孙：`full.Equals(root)` 或 `full.StartsWith(root + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`（根带或不带末尾分隔符都先 trim 掉尾部分隔符再比）。

### 4.2 线程

handler 仍在 WPF UI 线程登记；**枚举目录在 `Task.Run` 中完成**，避免网络盘/大目录卡住窗口和终端输出泵。通过校验后再 `Task.Run`。

### 4.3 错误码

| 条件 | `error.code` | 说明 |
|---|---|---|
| `path` / `root` 空 | `validation` | 路径不能为空 |
| `path` 不是 `root` 子孙（含 `..` 逃逸） | `validation` | 路径超出工作目录 |
| 路径不存在或不是目录 | `not_found` | 目录不存在 |
| `UnauthorizedAccessException` / `IOException` | `io` | 无法读取该目录 |

单条 `EnumerateFileSystemEntries` 失败：跳过该条目，不失败整层。层本身无权则整层 `io`。

前端：

- 根层 `not_found`：树体显示「目录不存在」。
- 根层 `io` / 其它：树体显示「无法读取」。
- 子层失败：该目录下显示「无法读取」，其它已展开节点保留。
- 刷新或切标签失败：toast 报错；若当前树已有节点则保留旧树，仅根还从未成功加载时换成错误态。
- 空目录：`entries: []`，该层显示「空目录」。

## 5. Core：`DirectoryLister`

新类型 `src/ForgeDeck.Core/Files/DirectoryLister.cs`，纯函数式服务，不依赖 WPF / ConfigStore / 终端：

```csharp
public sealed record FsEntry(string Name, string Path, bool IsDirectory, string Extension);
public sealed record FsListResult(string Path, IReadOnlyList<FsEntry> Entries);

public static class DirectoryLister
{
    public static FsListResult List(string path, string root);
}
```

- 校验失败抛 `BridgeException`（与 `ForgeDeckBridge` 其它方法同一程序集，由 `BridgeDispatcher` 封成 `validation` / `not_found` / `io`）。`UnauthorizedAccessException` / `IOException` 在 lister 内改抛 `BridgeException("io", ...)`，避免被 dispatcher 收成 `internal`。
- 使用 `Directory.EnumerateFileSystemInfos`（或等价），`FileAttributes.Directory` 判断目录。
- 不解析 junction / symlink 的目标来递归；条目该是目录就标 `isDirectory: true`，展开时再对那个 `path` 列一层。若展开目标不存在则 `not_found`。

`ForgeDeckBridge` 注册：

```csharp
Dispatcher.Register("fs.list", async p =>
{
    var path = p?.GetProperty("path").GetString() ?? "";
    var root = p?.GetProperty("root").GetString() ?? "";
    return await Task.Run(() => DirectoryLister.List(path, root));
});
```

## 6. 前端

### 6.1 组件

- `ui/src/FileTreePanel.tsx`：树 UI 与加载状态。props：`root: string | null`（激活会话 `workdir`，无会话为 `null`）。
- `ui/src/fileIcons.ts`：`fileBadge(name: string, isDirectory: boolean, extension: string) → { label: string; bg: string; fg: string } | { kind: 'folder' }`。
- `TerminalPanel`：外层改为两列，左 `FileTreePanel`，右原标签 + `term-body`。
- `App.tsx`：把激活会话的 `workdir` 传给树；`term-stage` 同时收窄 rail。无需新桥事件。

树状态（组件内）：

- `expanded: Set<string>`（规范化 path）
- `children: Map<string, FsEntry[] | 'loading' | 'error' | 'empty'>`
- `root` 变化：清空上述状态，对 `root` 发一次 `fs.list`
- 展开已缓存成功的目录：不重复请求
- 刷新：对 `root` 以及当前 `expanded` 中的每个 path 重新 `fs.list`（并行），失败策略见 4.3

### 6.2 图标

目录不走色块，用绿色 stroke 文件夹 SVG（`currentColor` = accent）。

文件先匹配**完整文件名**（忽略大小写），再匹配 `extension`：

| 匹配 | 色块 |
|---|---|
| 文件名 `.gitignore` | `GI` 灰 |
| 文件名等于 `Dockerfile`，或以 `Dockerfile.` 开头 | `DK` 灰蓝 |
| 文件名 `.env` / `.env.*` | `ENV` 橄榄 |
| 文件名 `go.mod` / `go.sum` | `GO` 青 |
| 文件名 `pom.xml`、`build.gradle`、`build.gradle.kts` | `JA` 橙红 |
| `ts` `tsx` | `TS` 蓝 `#3b82f6` / 白字 |
| `js` `jsx` `mjs` `cjs` | `JS` 金 `#ca8a04` / 深字 |
| `py` | `PY` 黄 `#eab308` / 深字 |
| `cs` `csproj` `sln` | `CS` 紫 `#8b5cf6` / 白字 |
| `java` | `JA` 橙红 `#ea580c` / 白字 |
| `go` | `GO` 青 `#0891b2` / 白字 |
| `css` `scss` `less` | `CSS` 浅紫 `#c084fc` / 深字 |
| `html` `htm` | `HTM` 橙 `#f97316` / 深字 |
| `json` | `JSON` 金 `#ca8a04` / 深字 |
| `md` `markdown` | `MD` 灰 `#64748b` / 白字 |
| `ps1` | `PS` 蓝 `#3178c6` / 白字 |
| `png` `jpg` `jpeg` `gif` `webp` `ico` `svg` | `IMG` 绿 `#16a34a` / 白字 |
| 其它有扩展名 | 扩展名截前 3 个字母大写，中性灰 |
| 无扩展名 | `•` 中性灰 |

色块：14×14px、圆角 3px、等宽 7px 字重 700，与工具列表字母标同一语言。颜色用十六进制（树在终端深底上，不用依赖 oklch 色块对比）。

### 6.3 MockBridge

`fs.list` 按 `path` 返回内置样例。root 必须与现有 Mock 会话 `workdir` 一致：`C:\\Projects\\atlas-web`（`terminal.create` / `createShell` 已写死该路径）。

- 根：`src/`（目录）、`go.mod`、`pom.xml`、`Program.cs`、`package.json`、`README.md`、`.gitignore`、`node_modules/`（目录）
- `src/`：`App.tsx`、`main.ts`、`app.css`、`Main.java`、`app.go`
- `node_modules/`：一两件占位文件，证明大目录可见且点开才「加载」
- 未知 path：空 `entries` 或与真桥一致的 `not_found`

样例会话的 `workdir` 与 Mock 树 root 对齐，避免 `npm run dev` 下列空。

## 7. 数据流

1. `terminal.create` / `createShell` 成功 → `refreshSessions` → 激活会话带 `workdir`。
2. `FileTreePanel` 见 `root` → `fs.list({ path: root, root })` → 渲染第一层（全部折叠，目录可点）。
3. 展开目录 → `fs.list({ path: dirPath, root })`；折叠只改 `expanded`。
4. 切换激活会话：`root` 变则重置状态并拉新的第一层；`root` 相同（两个会话同一工作目录）则不重置。
5. 刷新按钮：重列 `root` + 所有已展开 path。

## 8. 测试

`tests/ForgeDeck.Core.Tests`，临时目录，测完删除。

`DirectoryListerTests`：

- 混合文件/子目录：只返回一层；目录在前；`OrdinalIgnoreCase` 排序。
- 含 `.gitignore` 等点文件。
- `path == root` 合法；`path` 为 root 子目录合法。
- `path` 为 `root\..` 或其它盘路径 → `validation`。
- 不存在的目录 → `not_found`。
- 空目录 → `entries` 空列表。
- 文件的 `extension` 小写无点；目录 `extension` 为空。

`BridgeTests` 增补 `fs.list`：

- 正常列出临时目录。
- 缺 params / 逃逸路径返回 error 封包，入口不抛。

前端无单测；验证 = `npm run build` 通过 + 手动：会话页布局、展开/折叠、Java/C#/Go 色块、Mock 样例树。

## 9. 涉及文件

| 层 | 文件 |
|---|---|
| Core | `Files/DirectoryLister.cs`（新）、`Bridge/ForgeDeckBridge.cs` |
| 测试 | `DirectoryListerTests.cs`（新）、`BridgeTests.cs` |
| UI | `FileTreePanel.tsx`、`fileIcons.ts`（新）、`TerminalPanel.tsx`、`App.tsx`、`app.css`、`types.ts`、`bridge.ts` |

不改 csproj 版本号（无发版）。不改 ConfigStore / 会话模型。
