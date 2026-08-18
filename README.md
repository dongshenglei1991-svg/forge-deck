# ForgeDeck

<p align="center">
  <img src="ui/src/assets/logo.png" width="160" alt="ForgeDeck" />
</p>

本地 AI 编程工具快速启动器（仅 Windows）。

功能：扫描本机已安装的 AI 编程工具，为每个工具维护启动配置（参数 / 环境变量 / 工作目录 / 打开方式），支持内嵌终端打开与独立窗口打开，并记录工作目录历史。

## 功能现状（MVP）

- **扫描发现**：开始菜单 / 注册表 / PATH / 常见安装位置识别内置工具（Claude Code、Codex CLI、Grok Build、OpenCode、Copilot CLI 等），支持附加扫描目录与手动添加。
- **启动配置**：每个工具独立的参数、环境变量（每行 `KEY=VALUE`）、工作目录与运行方式，保存即持久化。
- **两种启动方式**：内嵌终端（新会话标签）与独立窗口（外部进程）。
- **内嵌终端**：xterm.js + ConPTY，多会话标签、输入输出、随窗口自适应尺寸。
- **工作目录历史**：记录最近使用的工作目录，配置面板下拉与文件夹选择弹窗共用。
- **设置**：默认 Shell、启动时自动扫描、附加扫描目录、退出确认与运行方式偏好。
- **反馈**：错误与成功操作以 Toast 提示（3.2 秒自动消失）。

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
