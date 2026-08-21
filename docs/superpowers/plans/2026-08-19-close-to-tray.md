# 关闭按钮行为（退出 / 最小化到托盘）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 设置关闭按钮行为（每次询问 / 直接退出 / 最小化到托盘）；首次点关闭弹出应用内询问框，勾选「以后不再提示」反填设置；托盘仅在隐藏时出现；单实例再启动唤回。

**架构：** Core 存 `CloseBehavior` + 纯函数 `CloseDecision.Resolve`；App 的 `OnClosing` 执行 Ask / HideToTray / Exit，并负责 NotifyIcon 与单实例；UI 设置卡片 + Modal；窗口方法走现有桥。

**技术栈：** .NET 8 WPF + WinForms NotifyIcon、xUnit、React 19 + TypeScript。规格：`docs/superpowers/specs/2026-08-19-close-to-tray-design.md`。

**测试：** `dotnet test`；前端 `npm run build`（无单测）。

---

## 文件

- 创建：`src/ForgeDeck.Core/CloseDecision.cs`
- 创建：`tests/ForgeDeck.Core.Tests/CloseDecisionTests.cs`
- 创建：`src/ForgeDeck.App/TrayIconHost.cs`
- 创建：`ui/src/ClosePromptModal.tsx`
- 修改：`src/ForgeDeck.Core/Models.cs`（枚举 + `AppSettings.CloseBehavior`）
- 修改：`tests/ForgeDeck.Core.Tests/ConfigStoreTests.cs`、`BridgeTests.cs`
- 修改：`src/ForgeDeck.App/MainWindow.xaml.cs`、`App.xaml.cs`、`ForgeDeck.App.csproj`
- 修改：`ui/src/types.ts`、`bridge.ts`、`SettingsView.tsx`、`App.tsx`、`app.css`

---

### 任务 1：Core 决策表与设置持久化

**文件：**
- 创建：`tests/ForgeDeck.Core.Tests/CloseDecisionTests.cs`
- 创建：`src/ForgeDeck.Core/CloseDecision.cs`
- 修改：`src/ForgeDeck.Core/Models.cs`
- 修改：`tests/ForgeDeck.Core.Tests/ConfigStoreTests.cs`
- 修改：`tests/ForgeDeck.Core.Tests/BridgeTests.cs`

- [ ] **步骤 1：编写失败的决策表测试**

```csharp
using ForgeDeck.Core;

namespace ForgeDeck.Core.Tests;

public class CloseDecisionTests
{
    [Theory]
    [InlineData(CloseBehavior.Ask, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.Exit, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.MinimizeToTray, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.Ask, false, CloseAction.Ask)]
    [InlineData(CloseBehavior.Exit, false, CloseAction.Exit)]
    [InlineData(CloseBehavior.MinimizeToTray, false, CloseAction.HideToTray)]
    public void Resolve_MatchesDecisionTable(CloseBehavior behavior, bool forceExit, CloseAction expected)
        => Assert.Equal(expected, CloseDecision.Resolve(behavior, forceExit));
}
```

- [ ] **步骤 2：运行确认失败（缺类型 / 未实现）**

```powershell
dotnet test --filter "FullyQualifiedName~CloseDecisionTests"
```

预期：编译失败，找不到 `CloseBehavior` / `CloseDecision`。

- [ ] **步骤 3：最小实现**

`Models.cs` 在 `OpenMode` 旁：

```csharp
public enum CloseBehavior { Ask, Exit, MinimizeToTray }
```

`AppSettings` 增加：

```csharp
public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;
```

`CloseDecision.cs`：

```csharp
namespace ForgeDeck.Core;

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

- [ ] **步骤 4：运行决策表测试通过**

```powershell
dotnet test --filter "FullyQualifiedName~CloseDecisionTests"
```

- [ ] **步骤 5：编写失败的持久化测试**

`ConfigStoreTests.cs` 追加：

```csharp
[Fact]
public void Load_OldSettingsWithoutCloseBehavior_DefaultsToAsk()
{
    var path = PathFor("config.json");
    File.WriteAllText(path, """{"version":1,"settings":{"defaultShell":"pwsh"}}""");
    var store = new ConfigStore(path);
    store.Load();
    Assert.Equal(CloseBehavior.Ask, store.Config.Settings.CloseBehavior);
}

[Fact]
public void SaveThenLoad_RoundTripsCloseBehavior()
{
    var path = PathFor("config.json");
    var store = new ConfigStore(path);
    store.Config.Settings.CloseBehavior = CloseBehavior.MinimizeToTray;
    store.Save();
    Assert.Contains("\"closeBehavior\": \"minimizeToTray\"", File.ReadAllText(path));
    var reloaded = new ConfigStore(path);
    reloaded.Load();
    Assert.Equal(CloseBehavior.MinimizeToTray, reloaded.Config.Settings.CloseBehavior);
}
```

`BridgeTests.SettingsGetSave_RoundTrip` 的 save payload 增加 `"closeBehavior":"minimizeToTray"`，并断言：

```csharp
Assert.Equal("minimizeToTray", ResultOf(saveResp!).GetProperty("settings").GetProperty("closeBehavior").GetString());
Assert.Equal(CloseBehavior.MinimizeToTray, _store.Config.Settings.CloseBehavior);
```

- [ ] **步骤 6：运行这些测试确认通过（属性已有默认值，应绿灯）**

```powershell
dotnet test --filter "FullyQualifiedName~CloseDecisionTests|FullyQualifiedName~ConfigStoreTests|FullyQualifiedName~BridgeTests.SettingsGetSave_RoundTrip"
```

- [ ] **步骤 7：Commit**

```powershell
git add src/ForgeDeck.Core/Models.cs src/ForgeDeck.Core/CloseDecision.cs tests/ForgeDeck.Core.Tests/CloseDecisionTests.cs tests/ForgeDeck.Core.Tests/ConfigStoreTests.cs tests/ForgeDeck.Core.Tests/BridgeTests.cs
git commit -m "feat(core): 新增关闭行为枚举与决策表"
```

---

### 任务 2：App 关窗分流、托盘、窗口方法

**文件：**
- 修改：`src/ForgeDeck.App/ForgeDeck.App.csproj`（`<UseWindowsForms>true</UseWindowsForms>`）
- 创建：`src/ForgeDeck.App/TrayIconHost.cs`
- 修改：`src/ForgeDeck.App/MainWindow.xaml.cs`

- [ ] **步骤 1：csproj 打开 WinForms**

在 `UseWPF` 旁加 `<UseWindowsForms>true</UseWindowsForms>`。

- [ ] **步骤 2：TrayIconHost**

用 `System.Windows.Forms.NotifyIcon`。`Show()` 用 `Icon.ExtractAssociatedIcon(Environment.ProcessPath)`（失败则跳过图标仍 Visible）。左键 `RestoreRequested`，右键菜单「显示主窗口」「退出」。第一次 `Show` 成功后 `ShowBalloonTip`：「ForgeDeck 仍在运行。点击托盘图标可恢复。」`Dispose` 时 `Visible=false` 并释放。捕获创建异常，返回 `bool` 给调用方。

- [ ] **步骤 3：MainWindow 关闭分流**

字段：`_forceExit`、`_tray`、`_trayRestoreState`。构造里 `Application.Current.SessionEnding += (_, _) => _forceExit = true;`。`WndProc` 增加 `WM_QUERYENDSESSION = 0x0011` 同样置 `_forceExit`。

`RegisterWindowMethods`：

- `window.hideToTray` → `HideToTray()`
- `window.exit` → `_forceExit = true; Close();`

`OnClosing` 开头：

```csharp
var action = CloseDecision.Resolve(_store.Config.Settings.CloseBehavior, _forceExit);
if (action == CloseAction.Ask)
{
    e.Cancel = true;
    if (Web.CoreWebView2 != null)
        _bridge.Dispatcher.Emit("window.close.prompt", new { });
    else
    {
        var choice = MessageBox.Show("关闭窗口？选「是」退出，选「否」最小化到托盘。", "ForgeDeck",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes) { _forceExit = true; Dispatcher.BeginInvoke(Close); }
        else HideToTray();
    }
    return;
}
if (action == CloseAction.HideToTray)
{
    e.Cancel = true;
    HideToTray();
    return;
}
// 现有会话确认……点否时 _forceExit = false
```

`HideToTray()`：已在托盘则 return。记住 `WindowState == Maximized ? Maximized : Normal`。`ShowInTaskbar = false; Hide();` 然后 `_tray.Show()`；失败则 MessageBox「无法创建托盘图标，窗口已隐藏，再次启动可恢复。」

`RestoreFromTray()` public：`ShowInTaskbar = true; Show(); WindowState = _trayRestoreState; Activate();` 必要时短暂 Topmost；`_tray.Hide()`。

真正退出路径 Dispose `_tray`。

Ask 时的系统框点「是」不要在 `OnClosing` 里同步 `Close()`（会重入），用 `BeginInvoke(Close)`。

- [ ] **步骤 4：编译 App**

```powershell
dotnet build src/ForgeDeck.App
```

预期：成功。

- [ ] **步骤 5：Commit**

```powershell
git add src/ForgeDeck.App
git commit -m "feat(app): 关闭按钮按设置询问、退出或最小化到托盘"
```

---

### 任务 3：单实例

**文件：**
- 修改：`src/ForgeDeck.App/App.xaml.cs`

- [ ] **步骤 1：OnStartup 抢 Mutex**

常量 `Local\ForgeDeck.SingleInstance` 与 `Local\ForgeDeck.ShowWindow`。

在 `base.OnStartup` **之前**：

- `new Mutex(true, mutexName, out created)`
- 未抢到：`EventWaitHandle.OpenExisting` 或 `new EventWaitHandle(..., name)` 后 `Set()`，再 `Shutdown()` 并 **return（不调用 base.OnStartup）**
- 抢到：`base.OnStartup`；后台线程 `WaitOne` 超时循环，收到信号则 `Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreFromTray())`

`OnExit`：取消等待、释放 Mutex。

第二实例退出码 0。

- [ ] **步骤 2：编译**

```powershell
dotnet build src/ForgeDeck.App
```

- [ ] **步骤 3：Commit**

```powershell
git add src/ForgeDeck.App/App.xaml.cs
git commit -m "feat(app): 单实例运行，再启动时唤回已有窗口"
```

---

### 任务 4：前端设置、询问框、Mock

**文件：**
- 修改：`ui/src/types.ts`、`bridge.ts`、`SettingsView.tsx`、`App.tsx`、`app.css`
- 创建：`ui/src/ClosePromptModal.tsx`

- [ ] **步骤 1：类型与 Mock**

`AppSettings` 增加 `closeBehavior: 'ask' | 'exit' | 'minimizeToTray'`。

Mock 样例 `closeBehavior: 'ask'`。`window.close`：

- `ask` → `this.emit('window.close.prompt', {})`
- `minimizeToTray` → `this.emit` 不方便 toast，返回后由 UI toast？规格：Mock 自己不能 toast。改为 emit prompt 仅当 ask；minimizeToTray 时 emit 一个？规格写 toast「已最小化到托盘（仅桌面壳生效）」。Mock 没有 toast 通道。处理：`window.hideToTray` / `minimizeToTray` 的 close **返回** `{ mocked: 'tray' }` 不够。更好：`window.close` 在 ask 时 emit prompt；`window.hideToTray` 空操作，由 `App.tsx` 在 mock 环境下？规格明确 toast。在 `window.hideToTray` 与 `window.close`+minimizeToTray 时 `this.emit('window.tray.mocked', {})`，App 不监听。最简单符合规格：Mock `handle` 里无法 toast。让 `window.hideToTray` 返回 `{ toast: '已最小化到托盘（仅桌面壳生效）' }`，App 调用后若有 toast 字段则显示——太 hack。

按规格在 Mock 的 `window.close`/`hideToTray` 用 `console.info` 不够。实现：`App.tsx` 调 `window.hideToTray` 后，若 `window.chrome?.webview` 不存在则 `toast('已最小化到托盘（仅桌面壳生效)')`。Mock `window.close` 按设置分流 emit prompt 或走同一 toast 路径：close+minimizeToTray 时 emit `window.close.prompt` 不行。Mock close：ask→emit prompt；minimizeToTray→emit `window.tray.mocked`；exit→noop。App 监听 `window.tray.mocked` 显示该 toast。这样点 X 与点询问框最小化在 mock 下都能看到提示。

落地选择（更少事件）：`App` 的 hide 处理统一 toast（始终「已最小化到托盘」在 mock 下；真壳托盘有气泡，不重复 toast）。检测：`typeof window !== 'undefined' && !window.chrome?.webview`。

- [ ] **步骤 2：SettingsView 新卡片**

本地 state `closeBehavior`，`[info]` effect 同步。保存时写入。卡片在两卡网格里 `style={{ gridColumn: '1 / -1' }}` 或 CSS `.setting-card.span-2 { grid-column: 1 / -1 }`。三个原生 radio。

- [ ] **步骤 3：ClosePromptModal**

复用 `Modal`。checkbox「以后不再提示」。次按钮「退出应用」，主按钮「最小化到托盘」。

- [ ] **步骤 4：App.tsx**

订阅 `window.close.prompt`，已打开则忽略。勾选且 `settingsInfo` 有值时先 `settings.save`（失败 toast 仍继续动作），再 `hideToTray` 或 `window.exit`。

- [ ] **步骤 5：radio CSS**

`.radio-list` / `.radio-row`，`accent-color` 用 accent 令牌。

- [ ] **步骤 6：build**

```powershell
cd ui; npm run build
```

预期：tsc 与 vite 成功。

- [ ] **步骤 7：Commit**

```powershell
git add ui
git commit -m "feat(ui): 关闭行为设置与首次关闭询问框"
```

---

### 任务 5：全量验证

- [ ] **步骤 1：后端测试**

```powershell
dotnet test
```

全部通过。

- [ ] **步骤 2：前端 build + lint**

```powershell
cd ui; npm run build; npm run lint
```

- [ ] **步骤 3：若有失败，修复后再提交**（不要空 commit）
