# ForgeDeck 设计文档 — 本地 AI 编程工具快速启动器

- 日期：2026-08-14
- 状态：待用户审查
- 目标平台：仅 Windows 10 1809+ / Windows 11

## 1. 背景与选型结论

本机安装的 AI 编程工具（Claude Code、Codex CLI、Cursor、Windsurf 等）越来越多，每个工具有各自的启动参数、环境变量和工作目录需求。ForgeDeck 是一个快速启动器：集中扫描已装工具，为每个工具维护启动配置，支持"内嵌终端打开"与"独立窗口打开"两种方式，并记录工作目录历史以便快速选择。

**已确认的技术路线（方案 A）**：WPF 壳 + WebView2 承载 Web 前端 + xterm.js 内嵌终端 + C# 后端。

关键事实与理由：

- Windows Terminal 本体无法嵌入第三方窗口；其底层引擎 ConPTY 是公开 API，任何应用可用。
- 内嵌终端采用 ConPTY（Windows Terminal 同款引擎）+ xterm.js（VS Code 内置终端同款渲染），体验等同"内嵌 Windows 自带终端"。
- 界面用 Web 技术实现，可 1:1 还原设计稿；后端 C# 匹配维护者的技术栈（Java/C#/Go）。

已否决的路线：WinUI 3 全原生（Windows Terminal 控件无官方 NuGet，需自行编译整个 terminal 仓库，工程成本过高）；Avalonia + XtermSharp（组件不再活跃维护）；Go + Wails（PTY 支持差）。

## 2. 目标与非目标

### 目标

1. 扫描本机已安装的 AI 编程工具（CLI 与 GUI），展示工具列表；支持手动添加自定义工具。
2. 为每个工具维护启动配置（Profile）：启动参数、环境变量、工作目录、打开方式（内嵌终端 / 独立窗口）。
3. 内嵌终端打开：多标签终端，工具运行在启动器内部。
4. 独立窗口打开：以指定参数/环境变量/工作目录拉起独立进程。
5. 工作目录历史：按工具记录最近使用的工作目录（上限 20 条），配置时可快速选择。

### 非目标（YAGNI）

- 不支持 macOS / Linux。
- 不做工具的安装、升级、卸载。
- 不做远程或多机管理。
- 不做插件/扩展系统。
- 不做工具内部状态的深度集成（如读取会话历史）。

## 3. 总体架构

三个工程，职责单向依赖（App → Core，App/Core 不依赖前端）：

```
┌─────────────────────────────────────────────┐
│ ForgeDeck.App（WPF .NET 8）                  │
│  主窗口 + WebView2 宿主 + Bridge（消息桥）    │
├──────────────────────────┬──────────────────┤
│ ForgeDeck.Core（.NET 8）  │ ui（React+TS）    │
│  工具扫描 / 配置存储 /     │  工具列表 / 配置  │
│  进程启动 / 终端会话       │  编辑 / 内嵌终端  │
│  （Porta.Pty→ConPTY）     │  （xterm.js）    │
└──────────────────────────┴──────────────────┘
```

- **ForgeDeck.App**：WPF 主窗口内嵌 WebView2（微软官方 Chromium 控件，Win10/11 系统自带运行时）。只做宿主和消息转发，不含业务逻辑。
- **ForgeDeck.Core**：纯类库，承载全部业务（扫描、配置、启动、终端会话管理），不依赖 WPF，可单元测试。
- **ui**：全部界面。Vite + React + TypeScript，终端用 `@xterm/xterm` + `@xterm/addon-fit`。

### 桥接协议（WebView2 WebMessage）

JSON 消息，双向：

- 请求/响应（JS → C#）：`{ "id": 1, "method": "tools.list", "params": {...} }`
  → `{ "id": 1, "result": {...} }` 或 `{ "id": 1, "error": { "code": "...", "message": "..." } }`
- 事件推送（C# → JS）：`{ "event": "terminal.data", "data": { "sessionId": "...", "chunk": "..." } }`

方法清单（初版）：

| 方法 | 说明 |
|---|---|
| `tools.list` / `tools.rescan` | 列出 / 重新扫描已装工具 |
| `tools.addManual` | 手动添加自定义工具 |
| `profiles.list` / `profiles.save` / `profiles.delete` | 启动配置的增删改查 |
| `workdirs.list` / `workdirs.add` / `workdirs.remove` | 工作目录历史 |
| `terminal.create` / `terminal.write` / `terminal.resize` / `terminal.kill` | 内嵌终端会话 |
| `launch.external` | 独立窗口启动 |
| `dialog.selectDirectory` | 弹出系统目录选择对话框 |

事件：`terminal.data`（输出流）、`terminal.exit`（进程退出）、`scan.progress`（扫描进度，可选）。

## 4. 模块设计（ForgeDeck.Core）

### 4.1 ToolScannerService — 工具扫描

数据源（每个数据源抽象为接口，单源失败跳过并记录，不影响整体）：

1. **内置已知工具目录**（嵌入资源 `known-tools.json`）：预置常见 AI 编程工具的检测规则，含可执行文件名、常见安装路径、类型（cli/gui）、默认启动命令模板。初版收录：Claude Code、Codex CLI、Gemini CLI、Cursor、Windsurf、Trae、Zed、VS Code、JetBrains 系列。
2. **常见安装目录枚举**：`%LOCALAPPDATA%\Programs\*`、`%PROGRAMFILES%\*`、`%PROGRAMFILES(X86)%\*`。
3. **PATH 扫描**：按已知 CLI 可执行名（`claude`、`codex`、`gemini` 等，含 `.exe/.cmd/.bat/.ps1`）逐个 `where` 查找。
4. **注册表卸载项**：`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`、`HKLM\` 同键及 `WOW6432Node`，取 DisplayName / InstallLocation / DisplayIcon。
5. **开始菜单快捷方式**：解析 `%APPDATA%\Microsoft\Windows\Start Menu\Programs` 与 ProgramData 对应目录下的 `.lnk`（COM IShellLink）。

合并去重规则：按可执行文件规范路径（大小写不敏感）去重；命中已知目录的展示预置名称与图标，未命中的以注册表 DisplayName 命名并标记为"未识别"。

输出 `ToolInfo`：`id`、`name`、`type`（cli/gui）、`exePath`、`iconSource`、`detectionSource`、`builtin`（是否预置）。

### 4.2 ConfigStore — 配置存储

- 位置：`%APPDATA%\ForgeDeck\config.json`。
- 启动时读取；每次变更原子写（写临时文件后 File.Replace），避免崩溃损坏。
- 损坏恢复：解析失败时将原文件改名为 `config.json.bak` 并重建默认配置。

Schema（version 字段供后续迁移）：

```json
{
  "version": 1,
  "tools": [
    { "id": "uuid", "name": "Claude Code", "type": "cli", "exePath": "C:\\...\\claude.exe",
      "iconSource": "builtin:claude-code", "detectionSource": "path", "builtin": true }
  ],
  "profiles": [
    { "id": "uuid", "toolId": "uuid", "name": "默认",
      "args": "--verbose", "env": { "ANTHROPIC_BASE_URL": "..." },
      "workdir": "D:\\projects\\demo", "openMode": "embedded" }
  ],
  "workdirHistory": { "<toolId>": ["D:\\projects\\demo", "..."], "__global__": ["..."] },
  "settings": { "defaultOpenMode": "embedded", "maxWorkdirHistory": 20 }
}
```

### 4.3 LaunchService — 启动

- 启动前校验：exe 存在、workdir 存在（可空，空则用用户主目录）、env 值支持 `%VAR%` 展开。
- **独立窗口**：`ProcessStartInfo`（`UseShellExecute=false`，`Arguments`、`WorkingDirectory`、`EnvironmentVariables`）。
- **内嵌终端**：构造最终命令行。CLI 工具多为 `.cmd`/`.ps1` shim（npm 全局安装），ConPTY 不能直接执行，需包装：`.cmd`/`.bat` → `cmd.exe /c "<path>" <args>`；`.ps1` → `pwsh -File`（无 pwsh 时 `powershell -File`）；`.exe` 直接执行。命令模板可被工具目录/Profile 覆盖。

### 4.4 TerminalSessionManager — 内嵌终端会话

- 用 `Porta.Pty`（MIT，封装 ConPTY，2026-01 仍活跃维护）创建 PTY 并 spawn 进程。
- 会话生命周期：`create(toolId, profileId)` → 返回 `sessionId`；输出异步读取，经桥推送 `terminal.data`；前端 `write`/`resize`/`kill`；进程退出推 `terminal.exit` 并回收资源。
- resize：前端 FitAddon 计算 cols/rows 后调用。
- 会话列表由管理器持有，App 退出时统一 kill（PTY 关闭会连带结束子进程树）。

### 4.5 Bridge — 消息桥（ForgeDeck.App）

- `WebMessageReceived` 反序列化 → 按 method 分发到 Core 服务 → 结果回传 `PostWebMessageAsJson`。
- `dialog.selectDirectory` 在 App 层实现（WPF `OpenFolderDialog`），属 UI 能力不下放 Core。

## 5. 前端（ui）

页面（最终结构以设计稿为准，此处为信息架构）：

1. **工具列表页**：工具卡片（图标、名称、类型），显示已配置的 Profile 数，点击进入工具详情。
2. **工具配置页**：Profile 列表 + 编辑器——参数输入框、环境变量键值对编辑器、工作目录选择（历史下拉 + 系统目录选择按钮 + 手动输入）、打开方式开关（内嵌/独立窗口）、启动按钮。
3. **内嵌终端区**：多标签，每标签一个 xterm.js 实例绑定一个会话。

开发模式：App 启动时读环境变量 `FORGEDECK_DEV=1` → WebView2 导航到 `http://localhost:5173`（Vite 热更新联调）；发布模式加载打包产物 `ui/dist`（作为 App 资源复制）。

## 6. 关键流程

1. **应用启动**：App 起 → Core 加载配置 → 前端加载 → `tools.list`（无缓存时触发扫描）→ 渲染工具列表。
2. **内嵌打开**：配置页点启动（openMode=embedded）→ `terminal.create {toolId, profileId}` → Core 校验并包装命令 → ConPTY spawn → 输出流推送到 xterm.js 渲染；用户输入 xterm → `terminal.write`；进程退出 → 标签标记"已退出"；成功后将 workdir 记入该工具历史。
3. **独立窗口打开**：`launch.external {toolId, profileId}` → Process.Start；成功后将 workdir 记入该工具历史。
4. **选择工作目录**：`dialog.selectDirectory`（原生对话框）或手动输入/历史选择 → `workdirs.add` 去重并置于首位，超限淘汰最旧。

## 7. 错误处理

| 场景 | 处理 |
|---|---|
| 单个扫描数据源失败（如注册表拒绝访问） | 跳过该源，记录日志，返回其余结果 |
| 启动时 exe 不存在 / workdir 无效 / env 值非法 | 桥返回 error（含字段定位），前端 toast 提示并跳到对应配置项 |
| 终端 spawn 失败 | 标签内显示错误信息与"重试"按钮 |
| 配置文件损坏 | 备份为 `.bak` 后重建默认配置，UI 提示 |
| App 退出时终端会话仍在 | 统一 kill，避免孤儿进程 |

## 8. 测试策略

- **ForgeDeck.Core（xUnit）**：
  - ConfigStore：读写 round-trip、原子写、损坏恢复。
  - LaunchService：命令包装（.cmd/.ps1/.exe 三种）、env 展开、路径校验、ProcessStartInfo 构造。
  - ToolScanner：以 fake 数据源（接口注入临时目录/注册表样例）验证合并去重与优先级。
  - TerminalSessionManager：spawn `cmd /c echo hello` 验证输出捕获、resize、退出事件（CI Windows 环境跑，ConPTY 依赖 Win10 1809+）。
- **Bridge**：方法注册表契约测试（每个方法可分发、响应形状正确）。
- **前端**：本期仅保证 `npm run build` 通过与手动验证；设计稿落地后可选加组件测试。

## 9. 工作区布局

```
ForgeDeck/
  ForgeDeck.sln
  src/ForgeDeck.App/        # WPF + WebView2 宿主 + Bridge
  src/ForgeDeck.Core/       # 业务服务（扫描/配置/启动/终端）
  tests/ForgeDeck.Core.Tests/
  ui/                       # Vite + React + TS + xterm.js
  docs/superpowers/specs/   # 设计文档
```

## 10. 开发与发布工作流

- 后端：`dotnet build` / `dotnet test`。
- 前端：`cd ui && npm run dev`（端口 5173）。
- 联调：`FORGEDECK_DEV=1 dotnet run --project src/ForgeDeck.App`。
- 发布：`npm run build` → dist 复制进 App 资源 → `dotnet publish`（单目录发布，后续再考虑安装器）。

## 11. 里程碑（待 UI 设计稿完成后细化实现计划）

1. M1 骨架打通：App↔前端桥接 hello world + 内嵌终端跑通 `cmd`。
2. M2 工具扫描 + 配置 CRUD（独立窗口启动可用）。
3. M3 内嵌终端整合 Profile 启动 + 工作目录历史。
4. M4 按 UI 设计稿完成全部界面与打磨。
