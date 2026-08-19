# ForgeDeck 设计文档 — 关闭按钮行为（退出 / 最小化到托盘）

- 日期：2026-08-19
- 状态：待用户审查
- 前置：`docs/superpowers/specs/2026-08-14-launcher-design.md`
- 方案：A（Core 决策表 + App 执行窗口/托盘/单实例 + UI 询问框与设置）

## 1. 背景

ForgeDeck 是常驻式本地启动器：内嵌终端会话可能还在跑，用户点关闭时不一定想结束进程。当前标题栏 X、`Alt+F4`、任务栏「关闭窗口」一律进入 `MainWindow.OnClosing`，最多再弹「有 N 个会话正在运行」确认，然后退出。没有托盘，也没有「关窗行为」设置。

已确认决策：

- 标题栏 X、`Alt+F4`、任务栏关闭走**同一设置**。托盘菜单「退出」始终真正退出。
- 先决定「关还是进托盘」；只有真正退出时，才走现有会话确认（`skipExitConfirm` 语义不变）。
- 首次以及设置为「每次询问」时，用应用内 Modal（与「手动添加工具」同一套），两个动作 +「以后不再提示」。
- 设置三项随时可改：每次询问 / 直接退出 / 最小化到托盘。
- 单实例：已藏到托盘后再点启动图标，唤回已有窗口。
- 托盘图标只在窗口因关闭行为被隐藏时出现；标题栏「最小化」仍进任务栏。

## 2. 目标与非目标

### 目标

1. 设置页新增「关闭行为」卡片，三项单选，持久化到现有 `config.json` 的 `settings`。
2. 默认 `ask`：点关闭时询问「退出应用」或「最小化到托盘」；勾选「以后不再提示」则按所选动作写回设置。
3. `minimizeToTray`：隐藏主窗口、从任务栏移除、显示托盘图标；会话继续跑。
4. `exit`：沿用现有 `OnClosing` 会话确认后退出。
5. 托盘：左键唤回；右键「显示主窗口」「退出」。进程内第一次进托盘时系统气泡提示一次。
6. 单实例 Mutex：第二份进程通知第一份显示窗口后以退出码 0 结束。

### 非目标

- 开机自启、托盘常驻、自定义托盘图标。
- 多开实例。
- 合并或删除 `skipExitConfirm`。
- 关闭动画、自定义询问文案。

## 3. 数据模型

`Models.cs` 新增 `enum CloseBehavior { Ask, Exit, MinimizeToTray }`。`AppSettings` 新增 `CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask`（旧配置缺字段时用属性默认值，无需 `config.json` 版本迁移，`version` 保持 1）：

```json
{
  "settings": {
    "defaultShell": "pwsh",
    "autoScanOnStartup": true,
    "extraScanDirs": [],
    "skipExitConfirm": false,
    "preferEmbedded": true,
    "maxWorkdirHistory": 20,
    "closeBehavior": "ask"
  }
}
```

| JSON 值 | C# 枚举 | 含义 |
|---|---|---|
| `ask` | `CloseBehavior.Ask` | 每次询问（含首次） |
| `exit` | `CloseBehavior.Exit` | 直接走退出路径 |
| `minimizeToTray` | `CloseBehavior.MinimizeToTray` | 隐藏到托盘 |

序列化沿用现有 `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`，与 `openMode` 等枚举一致。

`skipExitConfirm` 不改：只在**真正退出**且有运行中会话时生效。

`settings.save` 仍是整对象替换。前端写入 `closeBehavior` 时必须带上当前全部设置字段（与现有 `SettingsView` 的 `...info.settings` 一致）。询问框反填同样先展开当前 settings 再改这一项。

非法枚举字符串：反序列化失败，桥返回现有 validation 错误，不写盘。

## 4. Core：决策表

`CloseBehavior` 放在 `Models.cs`（与 `AppSettings.CloseBehavior` 一处）。决策纯函数放 `src/ForgeDeck.Core/CloseDecision.cs`，不依赖 WPF / WinForms。

```csharp
public enum CloseAction { Ask, Exit, HideToTray }

public static class CloseDecision
{
    public static CloseAction Resolve(CloseBehavior behavior, bool forceExit)
        => forceExit ? CloseAction.Exit
         : behavior == CloseBehavior.MinimizeToTray ? CloseAction.HideToTray
         : behavior == CloseBehavior.Ask ? CloseAction.Ask
         : CloseAction.Exit;
}
```

决策表：

| `forceExit` | `closeBehavior` | 结果 |
|---|---|---|
| true | 任意 | `Exit` |
| false | `minimizeToTray` | `HideToTray` |
| false | `ask` | `Ask` |
| false | `exit` | `Exit` |

注销 / 关机（`Application.SessionEnding` 或 `WM_QUERYENDSESSION`）视为 `forceExit = true`，避免藏到托盘挡住关机。

## 5. App：关闭流程

所有关窗入口进入现有 `OnClosing`（标题栏 X 仍调 `window.close` → `Window.Close()`；`Alt+F4`、任务栏关闭同理）。在会话确认之前插入决策：

1. 读点击当下的 `_store.Config.Settings.CloseBehavior` 与 `_forceExit`（含会话结束标志）。
2. `Resolve` 结果：
   - **Ask**：`e.Cancel = true`；若 WebView2 已就绪，发事件 `window.close.prompt`；否则退回系统 `MessageBox`（是 = 退出，否 = 进托盘，无「以后不再提示」）。前端 Modal 已打开时忽略重复事件，不叠框。WPF **不**长期锁询问标志：用户 Esc 关掉 Modal 后，下一次关闭应能再次询问。
   - **HideToTray**：`e.Cancel = true`，执行第 7 节隐藏逻辑（不 `Close()`）。
   - **Exit**：落入现有会话确认：无运行中会话 / `skipExitConfirm` / `_confirmedExit` → Dispose 终端并退出；否则原生「有 N 个会话正在运行…」；点否则 `e.Cancel = true` 且 `_forceExit = false`，窗口保持，不进托盘。

询问框动作（前端）：

| 操作 | 行为 |
|---|---|
| Esc / 点遮罩 / 点 × | 不调任何窗口方法，窗口保持 |
| 最小化到托盘 | 若勾选「以后不再提示」：先 `await settings.save({ ...settings, closeBehavior: "minimizeToTray" })`；然后 `window.hideToTray`。save 失败仍 hide，并 toast 保存失败 |
| 退出应用 | 若勾选：先 save `closeBehavior: "exit"`；然后 **`window.exit`（forceExit）**。禁止走 `window.close`（设置仍为 `ask` 时会再次询问） |

正在询问时用户又去设置页改了选项：询问框两个按钮语义不变（明确选最小化或强制退出），与当前设置解耦。

标题栏「最小化」仍只调 `window.minimize`，进任务栏，不进托盘。

## 6. 桥

窗口方法在 App `RegisterWindowMethods` 注册（Core 不依赖 WPF）。`MockBridge` 与 `ui/src/types.ts` 同步。

### 6.1 行为变更

| 方法 | 变化 |
|---|---|
| `window.close` | 保持 `Close()`，进入 `OnClosing`（因而走决策表） |
| `settings.get` / `settings.save` | `settings.closeBehavior` 随 `AppSettings` 往返 |

### 6.2 新增（App 层）

| 方法 | 参数 | 结果 | 说明 |
|---|---|---|---|
| `window.hideToTray` | 无 | `null` | 执行隐藏；已在托盘则空操作 |
| `window.exit` | 无 | `null` | 置 `_forceExit = true` 后 `Close()` |

事件：

| 事件 | 数据 | 说明 |
|---|---|---|
| `window.close.prompt` | 无 payload（`{}` 即可） | 仅 `Ask` 且 WebView2 就绪时发出 |

Mock：`settings` 样例数据补上 `closeBehavior: "ask"`。`window.close` 按当前 mock 设置分流（便于纯浏览器验收 UI，不等于桌面壳实现）：`ask` → emit `window.close.prompt`；`minimizeToTray` → toast「已最小化到托盘（仅桌面壳生效）」；`exit` → 空操作。`window.hideToTray` 同样 toast；`window.exit` 空操作。桌面壳行为以 App 为准。

## 7. 托盘

仅 App 层。`ForgeDeck.App.csproj` 打开 `UseWindowsForms`，使用 `System.Windows.Forms.NotifyIcon`。不把 WinForms 引进 Core。

- 仅当因关闭行为 Hide 时创建图标；窗口 Show 回来后 Dispose 图标。
- 图标：`Assets/app.ico`；提示文字「ForgeDeck」。
- 左键单击：唤回（见下）。
- 右键菜单：「显示主窗口」（唤回）、「退出」（`_forceExit` + `Close()`）。
- 本进程第一次成功进托盘：`ShowBalloonTip`，「ForgeDeck 仍在运行。点击托盘图标可恢复。」不写配置。
- 创建图标失败：窗口仍 Hide；MessageBox 或前端 toast「无法创建托盘图标，窗口已隐藏，再次启动可恢复」（单实例可唤回）。

隐藏：

1. 记住 `WindowState`（若当前是 `Minimized` 则记为 `Normal`，避免从托盘唤回后仍是最小化）。
2. `ShowInTaskbar = false`，`Hide()`。
3. 创建托盘图标。
4. **不** Dispose WebView、**不**杀终端会话。

唤回：

1. `ShowInTaskbar = true`，`Show()`，恢复记住的 `Normal` 或 `Maximized`。
2. `Activate()`；如未能前台，允许短暂 `Topmost` 切换以抢焦点。
3. 销毁托盘图标。

## 8. 单实例

在 `App.OnStartup` 里、调用 `base.OnStartup`（会加载 `StartupUri`）**之前**抢命名 Mutex `Local\ForgeDeck.SingleInstance`。

- 抢到：持有到进程退出；启动后台等待命名 `EventWaitHandle` `Local\ForgeDeck.ShowWindow`（UI 线程通过 `Dispatcher` 唤回主窗口）。
- 未抢到：`Set` 该事件后 `Shutdown()`，退出码 0，不创建主窗口。
- 已有进程无论前台还是托盘，都执行第 7 节唤回。

不做跨用户 `Global\` Mutex。

## 9. 前端

### 9.1 设置页

`SettingsView` 在现有两张卡片下方新增整行卡片「关闭行为」：

- 说明：「点击关闭按钮、按 Alt+F4 或从任务栏关闭窗口时。」
- 三个原生 radio（样式对齐现有 `.field` / `.switch-row`，不引入新组件库）：
  - 每次询问（关闭或最小化到托盘）→ `ask`
  - 直接退出 → `exit`
  - 最小化到托盘 → `minimizeToTray`
- 随「保存设置」一起提交。询问框反填后若用户正停留在设置页，沿用现有 `[info]` effect 回显。

「关闭应用时不弹会话确认」留在「终端偏好」，文案不改。

### 9.2 询问 Modal

复用 `Modal`。标题「关闭 ForgeDeck？」；副标题「可以把窗口藏到托盘，会话继续跑。」勾选「以后不再提示」。页脚次按钮「退出应用」、主按钮「最小化到托盘」。

`App.tsx` 订阅 `window.close.prompt`：已打开则忽略。`settingsInfo` 尚未加载时不允许勾选反填（无当前 settings 可展开）；仍可执行 hide / exit。

## 10. 错误处理与边界

| 场景 | 处理 |
|---|---|
| WebView2 未就绪时需要询问 | 取消关闭，系统 Yes/No：是退出、否进托盘 |
| 托盘图标创建失败 | 仍 Hide；提示用户再次启动可恢复 |
| `settings.save` 在询问框反填时失败 | 仍执行用户选的 hide / exit，toast 保存失败 |
| 会话确认点「否」 | 窗口保持，`_forceExit` 复位，不进托盘 |
| 注销 / 关机 | `forceExit`，绝不藏托盘 |
| 任务管理器「结束进程」 | 无法拦截 |
| 任务管理器「结束任务」 | 走 `WM_CLOSE`，因而走关闭行为设置 |
| 关窗瞬间设置被改 | 以 `OnClosing` 读到的配置为准 |
| 第二实例通知失败 | 第二实例仍退出；用户可再点托盘（若图标在） |

## 11. 测试

### Core（xUnit，必做）

- `CloseDecisionTests`：决策表全覆盖（`forceExit=true` 对三种行为均为 `Exit`；`forceExit=false` 时三种行为分别对应 `Ask` / `Exit` / `HideToTray`）。
- `ConfigStore`：写入含 `closeBehavior` 的 settings 再读回；旧 JSON 无该字段时为 `ask`；落盘 JSON 含 `"closeBehavior": "minimizeToTray"`。
- `BridgeTests.SettingsGetSave_RoundTrip`：payload 增加 `closeBehavior`，断言往返。

### 前端

无单测。`npm run build` 通过。Mock 下：设置三选一能保存；点标题栏 X 按第 6 节 Mock 分流能弹出询问框。

### 真机（实现后手动）

1. 默认点 X → 询问框；不勾选，最小化 → 托盘；再点 X 仍询问。
2. 勾选 + 退出 → 设置变为「直接退出」；再启动后点 X 不再询问，有会话时仍弹会话确认。
3. 设置改成「最小化到托盘」→ X / `Alt+F4` / 任务栏关闭都进托盘。
4. 托盘左键唤回，图标消失；右键退出走会话确认。
5. 进托盘后再点启动图标 → 唤回，不出现第二窗口。
6. 标题栏最小化仍进任务栏，无托盘图标。
7. 设置「直接退出」且无会话 → 直接关进程。

## 12. 涉及文件（预期）

- 创建：`src/ForgeDeck.Core/CloseDecision.cs`，`tests/ForgeDeck.Core.Tests/CloseDecisionTests.cs`
- 修改：`Models.cs`（`CloseBehavior` + `AppSettings`）、`MainWindow.xaml.cs`、`App.xaml.cs`、`ForgeDeck.App.csproj`、`BridgeTests.cs`、`ConfigStoreTests.cs`、`ui/src/types.ts`、`bridge.ts`、`SettingsView.tsx`、`App.tsx`、`app.css`（radio 行）。复用 `Modal.tsx`，不改其 API。

不改版本号（行为设置，非发版项）。
