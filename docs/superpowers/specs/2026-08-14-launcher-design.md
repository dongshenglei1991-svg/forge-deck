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
5. 工作目录历史：记录最近使用的工作目录（全局一份，上限 20 条，菜单展示最近 5 条），配置时可快速选择。
6. 设置页：附加扫描目录、启动时自动扫描、默认 Shell（新空白会话用）、关闭应用时是否弹出会话确认。

### UI 视觉契约

界面以 `docs/design/Web-Prototype/ai-tool-launcher.html` 为像素级契约：深色 oklch 令牌体系、衬线标题字 + 等宽点缀、四视图导航（快速启动/工具库/终端会话/设置）、底部内嵌终端（工具库与设置视图下自动隐藏）、一个弹窗（手动添加工具；原工作文件夹弹窗已改为系统原生对话框）。工作目录选择使用**系统原生目录选择对话框**（2026-08-16 用户决策变更，原应用内弹窗已移除）；最近目录下拉保留。通知铃铛与头像为静态装饰。

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
| `terminal.create` / `terminal.createShell` / `terminal.write` / `terminal.resize` / `terminal.kill` | 内嵌终端会话（createShell 按 settings.defaultShell 开空白会话） |
| `sessions.list` | 内嵌会话列表与状态 |
| `launch.external` | 独立窗口启动 |
| `settings.get` / `settings.save` | 设置读写（get 附带常用目录与用户名，供前端渲染） |
| `app.info` | 版本号、最近使用、上次扫描时间 |

事件：`terminal.data`（输出流）、`terminal.exit`（进程退出）、`scan.progress`（扫描进度，可选）。

## 4. 模块设计（ForgeDeck.Core）

### 4.1 ToolScannerService — 工具扫描

数据源（每个数据源抽象为接口，单源失败跳过并记录，不影响整体）：

1. **内置已知工具目录**（C# 静态类 `KnownTools`）：预置常见 AI 编程工具的检测规则，含可执行文件名、安装位置提示（含类别标签，如"npm 全局""用户目录""Python Scripts"）、类型（cli/gui）、恢复参数。初版收录：Claude Code、Codex CLI、Gemini CLI、Aider、Cursor、Cursor Agent、Windsurf、Trae、Zed、VS Code。安装位置提示覆盖：`%APPDATA%\npm`、`%USERPROFILE%\.local\bin`、`%LOCALAPPDATA%\Programs\*`、Python Scripts 目录、`%PROGRAMFILES%\*`。
2. **PATH 扫描**：按已知可执行名（含 `.exe/.cmd/.bat/.ps1`）在 PATH 目录中探测。
4. **注册表卸载项**：`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`、`HKLM\` 同键及 `WOW6432Node`，取 DisplayName / InstallLocation / DisplayIcon，按名称匹配已知工具。
5. **开始菜单快捷方式**：解析 `%APPDATA%\Microsoft\Windows\Start Menu\Programs` 与 ProgramData 对应目录下的 `.lnk`（COM IShellLink）。
6. **附加扫描目录**：来自设置页 `settings.extraScanDirs`。

合并去重规则：按可执行文件规范路径（大小写不敏感）去重，同一已知工具只在第一个命中的数据源出现（数据源优先级同上编号）；命中已知目录的展示预置名称与图标，未命中的以注册表 DisplayName 命名并标记为"未识别"。

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
    { "id": "uuid", "name": "Claude Code", "type": "cli", "exePath": "C:\\...\\claude.cmd",
      "source": "npm 全局", "builtin": true, "manual": false }
  ],
  "profiles": [
    { "id": "uuid", "toolId": "uuid", "name": "默认",
      "args": "--verbose", "env": { "ANTHROPIC_BASE_URL": "..." },
      "workdir": "D:\\projects\\demo", "openMode": "embedded", "autoRestore": false }
  ],
  "workdirHistory": { "__global__": ["D:\\projects\\demo", "..."] },
  "settings": { "defaultShell": "pwsh", "autoScanOnStartup": true, "extraScanDirs": [],
                "skipExitConfirm": false, "preferEmbedded": true, "maxWorkdirHistory": 20 },
  "lastScanAt": "2026-08-14T12:00:00Z",
  "lastUsed": { "toolId": "uuid", "workdir": "D:\\projects\\demo" }
}
```

说明：工作目录历史为**全局单列表**（对齐设计稿），键固定 `__global__`；`autoRestore` 对应设计稿"启动时自动恢复上次会话"开关，开启后启动命令自动追加该工具目录定义的恢复参数（如 Claude Code 追加 `--continue`），无恢复参数的工具不显示该开关；`skipExitConfirm` 对应"关闭应用时保留会话"开关，语义为**退出时不弹确认**（内嵌会话随应用结束，与 Windows Terminal 关闭行为一致）。

### 4.3 LaunchService — 启动

- 启动前校验：exe 存在、workdir 存在（可空，空则用用户主目录）、env 值支持 `%VAR%` 展开。
- **独立窗口**：`ProcessStartInfo`（`UseShellExecute=false`，`Arguments` 传用户原始参数串、`WorkingDirectory`、`EnvironmentVariables`）；`.cmd`/`.bat` 可由 CreateProcess 直接启动。
- **内嵌终端**：构造最终命令行。CLI 工具多为 `.cmd`/`.ps1` shim（npm 全局安装），ConPTY 不能直接执行，需包装：`.cmd`/`.bat` → `cmd.exe /c <path> <args>`；`.ps1` → `pwsh -File`（无 pwsh 时 `powershell -File`）；`.exe` 直接执行。参数经引号感知的分词器拆分；`autoRestore` 开启时追加工具目录的恢复参数。

### 4.4 TerminalSessionManager — 内嵌终端会话

- 用 `Porta.Pty`（MIT，封装 ConPTY，2026-01 仍活跃维护）创建 PTY 并 spawn 进程。API：`PtyProvider.SpawnAsync(PtyOptions)` → `IPtyConnection`（`ReaderStream`/`WriterStream`/`Resize`/`Kill`/`ProcessExited`）；Windows 侧用 Job Object 隔离进程树。环境变量传**与当前进程合并后的全量**，避免子进程丢失 PATH。
- 会话生命周期：`create(toolId, profileId)` → 返回 `sessionId`；输出异步读取，经桥推送 `terminal.data`；前端 `write`/`resize`/`kill`；进程退出推 `terminal.exit` 并回收资源。
- resize：前端 FitAddon 计算 cols/rows 后调用。
- 会话列表由管理器持有，App 退出时统一 kill（PTY 关闭会连带结束子进程树）。

### 4.5 Bridge — 消息桥（ForgeDeck.App）

- `WebMessageReceived` 反序列化 → 按 method 分发到 Core 服务 → 结果回传 `PostWebMessageAsJson`。
- `dialog.selectDirectory`（2026-08-16 恢复）：App 层用 WPF `OpenFolderDialog` 弹系统目录选择框，返回 `{path}` 或 null（取消）。

## 5. 前端（ui）

页面结构对齐设计稿四视图（单页应用内切换，非多路由）：

1. **快速启动（launcher）**：头部 + "手动添加工具"按钮；三个指标卡（最近使用 / 已识别工具 / 活跃会话）；双栏工作区——左"本机工具"列表（含重新扫描行），右"启动配置"面板（参数输入、工作目录控件（输入框 + 最近菜单 + 文件夹选择弹窗）、环境变量 KEY=VALUE 文本域、"自动恢复会话"开关、运行方式二选一（内嵌终端/独立窗口）、启动/保存按钮）。
2. **工具库（tools）**：表格（工具 / 可执行文件 / 来源 / 默认方式 / 状态）。
3. **终端会话（sessions）**：会话卡片网格（名称、工作目录、运行状态）+ 新建空白会话。
4. **设置（settings）**：工具发现（附加扫描目录、启动时自动扫描）与终端偏好（默认 Shell、关闭应用时保留会话、优先使用内嵌终端）。
5. **底部内嵌终端**：多标签 + 新建按钮，每标签一个 xterm.js 实例绑定一个会话；工具库与设置视图下整个终端区隐藏。
6. **弹窗**：手动添加工具（名称 + 可执行路径）；选择工作文件夹（路径输入 + 常用位置网格 = 系统常用目录 + 工作目录历史）。

样式实现：设计稿 `<style>` 整体迁移为全局 CSS，React 组件沿用设计稿的类名与 DOM 结构，保证像素级还原；新增的 Toast 与 xterm 容器样式以同一套令牌补充。

开发模式：App 启动时读环境变量 `FORGEDECK_DEV=1` → WebView2 导航到 `http://localhost:5173`（Vite 热更新联调）；发布模式加载打包产物 `ui/dist`（作为 App 资源复制到 wwwroot）。纯浏览器开发（无 WebView2 宿主）时桥接客户端自动降级为 Mock 实现（内置设计稿同款样例数据）。

## 6. 关键流程

1. **应用启动**：App 起 → Core 加载配置 → 前端加载 → `tools.list`（无缓存时触发扫描）→ 渲染工具列表。
2. **内嵌打开**：配置页点启动（openMode=embedded）→ `terminal.create {toolId, profileId}` → Core 校验并包装命令 → ConPTY spawn → 输出流推送到 xterm.js 渲染；用户输入 xterm → `terminal.write`；进程退出 → 标签标记"已退出"；成功后将 workdir 记入该工具历史。
3. **独立窗口打开**：`launch.external {toolId, profileId}` → Process.Start；成功后将 workdir 记入该工具历史。
4. **选择工作目录**：点浏览按钮弹出应用内"选择工作文件夹"弹窗（路径输入 + 常用位置 = 系统常用目录与工作目录历史），或点下拉按钮直接选最近 5 条；确认后写入输入框，待启动时统一入史。
5. **重新扫描**：`tools.rescan` → 以扫描结果替换自动识别的工具（手动添加的保留）→ 刷新列表与"上次扫描"时间。

## 7. 错误处理

| 场景 | 处理 |
|---|---|
| 单个扫描数据源失败（如注册表拒绝访问） | 跳过该源，记录日志，返回其余结果 |
| 启动时 exe 不存在 / workdir 无效 / env 值非法 | 桥返回 error（含字段定位），前端 toast 提示并跳到对应配置项 |
| 终端 spawn 失败 | 标签内显示错误信息与"重试"按钮 |
| 配置文件损坏 | 备份为 `.bak` 后重建默认配置，UI 提示 |
| App 退出时终端会话仍在 | 若 `skipExitConfirm=false` 弹原生确认框"有 N 个会话正在运行，退出将结束它们"；确认后统一 kill，避免孤儿进程 |
| 桥接调用返回 error | 前端 Toast 展示错误信息（带错误码），可定位配置问题的附字段提示 |

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
