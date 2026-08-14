# ForgeDeck

本地 AI 编程工具快速启动器（仅 Windows）。

功能：扫描本机已安装的 AI 编程工具，为每个工具维护启动配置（参数 / 环境变量 / 工作目录 / 打开方式），支持内嵌终端打开与独立窗口打开，并记录工作目录历史。

## 技术栈

- **壳**：WPF（.NET 8）+ WebView2
- **后端**：C# 类库 `ForgeDeck.Core`（工具扫描、配置存储、进程启动、ConPTY 终端会话）
- **前端**：Vite + React + TypeScript + xterm.js（`ui/`）

## 开发

```bash
# 后端
dotnet build
dotnet test

# 前端（热更新开发服务器，端口 5173）
cd ui && npm install && npm run dev

# 联调：以开发模式启动桌面壳，加载 Vite 开发服务器
FORGEDECK_DEV=1 dotnet run --project src/ForgeDeck.App
```

## 发布

```bash
cd ui && npm run build
dotnet publish src/ForgeDeck.App -c Release
```

## 文档

- [设计文档](docs/superpowers/specs/2026-08-14-launcher-design.md)
