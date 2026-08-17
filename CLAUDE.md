# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

ForgeDeck：仅 Windows 的本地 AI 编程工具快速启动器。WPF（.NET 8）壳 + WebView2 承载 React 前端，业务逻辑全部在 C# 类库 ForgeDeck.Core。仓库内文档、注释、UI 文案均为中文。

## 常用命令

后端（仓库根目录）：

```bash
dotnet build
dotnet test                                                  # 全部测试（xUnit）
dotnet test --filter "FullyQualifiedName~BridgeTests"       # 单个测试类
dotnet test --filter "FullyQualifiedName~BridgeTests.MalformedJson_ReturnsParseError"  # 单个测试
```

前端（ui/ 目录，前端无单测，验证 = build 通过 + 手动）：

```bash
npm run dev      # Vite 开发服务器（5173）；纯浏览器打开时桥自动降级为 MockBridge（内置样例数据），可脱离 WPF 宿主开发 UI
npm run build    # tsc -b && vite build，产物 ui/dist 即 WPF 打包内容
npm run lint     # oxlint
```

联调（热更新）：先 `cd ui && npm run dev`，再以开发模式启动桌面壳（读 `FORGEDECK_DEV=1`，WebView2 导航到 localhost:5173 而非打包的 wwwroot）：

```powershell
# PowerShell（README 里的 `FORGEDECK_DEV=1 dotnet run ...` 是 bash 语法）
$env:FORGEDECK_DEV='1'; dotnet run --project src/ForgeDeck.App
```

发布（顺序不能反——App.csproj 把 ui/dist 复制进 wwwroot）：

```bash
cd ui && npm run build
dotnet publish src/ForgeDeck.App -c Release
```

## 架构

三个工程，单向依赖 App → Core，二者都不依赖前端；C# 与 UI 之间只通过 JSON 消息桥（WebView2 WebMessage）通信，无直接调用：

- `src/ForgeDeck.App` — 纯宿主：MainWindow + WebView2、窗口/系统对话框类桥方法，不含业务逻辑
- `src/ForgeDeck.Core` — 全部业务，不依赖 WPF（可单测）：
  - `Scanning/` — 各 `IScanSource`，注入顺序即优先级（KnownDirs→Path→Registry→StartMenu→ExtraDirs）；`ToolScanner` 按全路径 OrdinalIgnoreCase 去重、单源失败隔离；`KnownTools.cs` 是已知工具注册表（可执行名 + 安装位置提示 + 恢复参数），加新工具改这里
  - `Config/` — `ConfigStore` 持久化到 `%APPDATA%\ForgeDeck\config.json`（tmp+move 原子写；损坏→备份 .bak 回退默认）；`WorkdirHistoryService` 工作目录历史
  - `Launching/LaunchService` — 命令包装（.cmd/.ps1/.exe 三种宿主）、env 解析、独立窗口启动
  - `Terminal/TerminalSessionManager` — Porta.Pty（ConPTY）会话；约定：`VerbatimCommandLine=true` + 仅给含空白的参数加引号
  - `Bridge/` — `ForgeDeckBridge` 注册业务方法；`BridgeDispatcher` 分发（入口全程不抛异常，一切封包为 error 响应）
- `ui/` — React 19 + TypeScript + xterm.js；`App.tsx` 集中状态与视图切换；`bridge.ts` 是桥客户端

### 桥：加一个方法要动 3 处

请求/响应（JS→C#）`{id, method, params}` → `{id, result | error}`；事件推送（C#→JS）`{event, data}`。

- 业务方法 → `ForgeDeckBridge.RegisterMethods()`（Core）
- 窗口/系统对话框方法 → `MainWindow.RegisterWindowMethods()`（App 层；Core 不得依赖 WPF）
- Mock 实现 → `ui/src/bridge.ts` 的 `MockBridge.handle()` —— 纯浏览器开发靠它，漏加会破坏 `npm run dev` 下的行为一致性
- 类型 → `ui/src/types.ts`

线程模型（详见 ForgeDeckBridge 头注释）：桥 handler 由 WPF UI 线程串行调用，ConfigStore 访问无需加锁；唯一例外 `tools.rescan` —— 先快照配置、线程池扫描合并、回 UI 线程写回；终端事件来自后台线程，经 `MainWindow.Post` 转回 UI 线程。

### 不能顺手"修掉"的承载性设计

- WebView2 环境在 `MainWindow.OnWindowLoaded` 手动创建：部分环境下控件自动初始化路径会无限挂起（白屏）。发布模式以 file:// 直载 wwwroot，因此 Vite `base: './'`（绝对路径 /assets 在 file:// 下白屏）与 `--allow-file-access-from-files`（file 页面加载 ES module 的 CORS）都是必需的
- `tools.rescan` 合并按 ExePath（OrdinalIgnoreCase）复用旧条目以保 Id 稳定：profile 与 lastUsed 按 Id 关联工具，每次重扫重铸 Id 会让它们静默失联；Manual（手动添加）条目在重扫中永远保留
- 版本号存在于三处：两个 .csproj 与 `ForgeDeckBridge.Version`，需一起改

### 约定

- 视觉契约：`docs/design/Web-Prototype/ai-tool-launcher.html`（像素级，样式迁移为 `ui/src/app.css`）；规格与计划文档在 `docs/superpowers/`
- 测试（xUnit，tests/ForgeDeck.Core.Tests）：注册表测试写 `HKCU\Software\ForgeDeckTests` 并在 Dispose 清理；终端测试 spawn 真实进程（Dispose 统一释放）；Bridge 测试注入临时目录 ConfigStore + `FixedSource` 假扫描源
