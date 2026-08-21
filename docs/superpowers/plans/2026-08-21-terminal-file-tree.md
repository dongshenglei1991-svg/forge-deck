# 终端会话工作目录文件树 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 进入「终端会话」后，左侧展示当前激活会话启动工作目录的文件树（按层懒加载、字母色块图标），导航收成 64px 图标栏，右侧仍是标签 + xterm。

**架构：** Core `DirectoryLister.List(path, root)` 只列一层并校验子孙路径；桥方法 `fs.list` 在线程池枚举；React `FileTreePanel` 按激活会话 `workdir` 拉根层、展开再请求；图标纯前端映射。不跟踪 `cd`、不监视磁盘。

**技术栈：** .NET 8、xUnit、React 19 + TypeScript。规格：`docs/superpowers/specs/2026-08-21-terminal-file-tree-design.md`。

**测试：** `dotnet test`（Core）；前端无单测，验证 = `cd ui && npm run build` + 手动（含 MockBridge）。

---

## 文件

- 创建：`src/ForgeDeck.Core/Files/DirectoryLister.cs` — 一层列举 + 路径校验
- 创建：`tests/ForgeDeck.Core.Tests/DirectoryListerTests.cs`
- 创建：`ui/src/fileIcons.ts` — 文件名/扩展名 → 色块
- 创建：`ui/src/FileTreePanel.tsx` — 树 UI 与加载状态
- 修改：`src/ForgeDeck.Core/Bridge/ForgeDeckBridge.cs` — 注册 `fs.list`
- 修改：`tests/ForgeDeck.Core.Tests/BridgeTests.cs` — 桥测试
- 修改：`ui/src/types.ts` — `FsEntry` / `FsListResult`
- 修改：`ui/src/bridge.ts` — MockBridge `fs.list`
- 修改：`ui/src/TerminalPanel.tsx` — 左树右终端
- 修改：`ui/src/App.tsx` — 传入 `workdir` / `onError`；`term-stage` 收窄 rail
- 修改：`ui/src/app.css` — 会话页图标栏 + 文件树样式

不改 csproj 版本、ConfigStore、会话模型。SDK 风格工程会自动编译新 `.cs`。

---

### 任务 1：DirectoryLister（一层列举与路径守卫）

**文件：**
- 创建：`tests/ForgeDeck.Core.Tests/DirectoryListerTests.cs`
- 创建：`src/ForgeDeck.Core/Files/DirectoryLister.cs`

- [ ] **步骤 1：编写失败的测试**

```csharp
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Files;

namespace ForgeDeck.Core.Tests;

public class DirectoryListerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));

    public DirectoryListerTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "Lib"));
        File.WriteAllText(Path.Combine(_root, "src", "App.tsx"), "");
        File.WriteAllText(Path.Combine(_root, "README.md"), "");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "");
        File.WriteAllText(Path.Combine(_root, "A.txt"), "");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "");
        File.WriteAllText(Path.Combine(_root, "Program.cs"), "");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void List_Root_OneLevel_DirsFirst_OrdinalIgnoreCase()
    {
        var result = DirectoryLister.List(_root, _root);
        Assert.Equal(Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar), result.Path);
        Assert.DoesNotContain(result.Entries, e => e.Name == "App.tsx");
        Assert.Equal(new[] { "empty", "Lib", "src", ".gitignore", "A.txt", "b.txt", "Program.cs", "README.md" },
            result.Entries.Select(e => e.Name).ToArray());
        Assert.True(result.Entries.Take(3).All(e => e.IsDirectory));
        Assert.True(result.Entries.Skip(3).All(e => !e.IsDirectory));
    }

    [Fact]
    public void List_IncludesDotFiles_AndLowercaseExtension()
    {
        var git = DirectoryLister.List(_root, _root).Entries.Single(e => e.Name == ".gitignore");
        Assert.False(git.IsDirectory);
        Assert.Equal("", git.Extension);

        var cs = DirectoryLister.List(_root, _root).Entries.Single(e => e.Name == "Program.cs");
        Assert.Equal("cs", cs.Extension);

        var src = DirectoryLister.List(_root, _root).Entries.Single(e => e.Name == "src");
        Assert.True(src.IsDirectory);
        Assert.Equal("", src.Extension);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "src")), src.Path);
    }

    [Fact]
    public void List_ChildDirectory_IsAllowed()
    {
        var src = Path.Combine(_root, "src");
        var result = DirectoryLister.List(src, _root);
        Assert.Single(result.Entries);
        Assert.Equal("App.tsx", result.Entries[0].Name);
        Assert.Equal("tsx", result.Entries[0].Extension);
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmptyEntries()
    {
        var result = DirectoryLister.List(Path.Combine(_root, "empty"), _root);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void List_EmptyPath_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => DirectoryLister.List("", _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径不能为空", ex.Message);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => DirectoryLister.List(_root, "  ")).Code);
    }

    [Fact]
    public void List_PathOutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => DirectoryLister.List(parent, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);

        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => DirectoryLister.List(win, _root)).Code);
    }

    [Fact]
    public void List_MissingOrFile_ThrowsNotFound()
    {
        var missing = Assert.Throws<BridgeException>(() => DirectoryLister.List(Path.Combine(_root, "nope"), _root));
        Assert.Equal("not_found", missing.Code);
        Assert.Equal("目录不存在", missing.Message);

        var file = Path.Combine(_root, "README.md");
        Assert.Equal("not_found", Assert.Throws<BridgeException>(() => DirectoryLister.List(file, _root)).Code);
    }
}
```

排序期望（`OrdinalIgnoreCase` 先不变式大写再比）：目录 `empty` / `Lib` / `src`（`EMPTY` < `LIB` < `SRC`），然后文件 `.gitignore`、`A.txt`、`b.txt`、`Program.cs`、`README.md`（`.` < `A` < `B` < `P` < `R`）。

- [ ] **步骤 2：运行确认失败**

```powershell
dotnet test --filter "FullyQualifiedName~DirectoryListerTests"
```

预期：编译失败，找不到 `DirectoryLister` / `FsEntry` / `FsListResult`。

- [ ] **步骤 3：最小实现**

创建 `src/ForgeDeck.Core/Files/DirectoryLister.cs`：

```csharp
using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public sealed record FsEntry(string Name, string Path, bool IsDirectory, string Extension);
public sealed record FsListResult(string Path, IReadOnlyList<FsEntry> Entries);

public static class DirectoryLister
{
    public static FsListResult List(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            throw new BridgeException("validation", "路径不能为空");

        var fullRoot = Normalize(root);
        var fullPath = Normalize(path);
        if (!IsUnderRoot(fullPath, fullRoot))
            throw new BridgeException("validation", "路径超出工作目录");
        if (!Directory.Exists(fullPath))
            throw new BridgeException("not_found", "目录不存在");

        List<FsEntry> entries;
        try
        {
            entries = new List<FsEntry>();
            foreach (var info in new DirectoryInfo(fullPath).EnumerateFileSystemInfos())
            {
                try
                {
                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    var ext = isDir ? "" : info.Extension.TrimStart('.').ToLowerInvariant();
                    entries.Add(new FsEntry(info.Name, info.FullName, isDir, ext));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // 单条失败跳过，不打爆整层
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new BridgeException("io", "无法读取该目录");
        }
        catch (IOException)
        {
            throw new BridgeException("io", "无法读取该目录");
        }

        entries.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return new FsListResult(fullPath, entries);
    }

    private static string Normalize(string p)
    {
        try
        {
            return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new BridgeException("validation", "路径不能为空");
        }
    }

    private static bool IsUnderRoot(string full, string root) =>
        full.Equals(root, StringComparison.OrdinalIgnoreCase)
        || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **步骤 4：运行测试通过**

```powershell
dotnet test --filter "FullyQualifiedName~DirectoryListerTests"
```

若排序断言失败，先打印 `string.Join(",", result.Entries.Select(e => e.Name))` 对照 OrdinalIgnoreCase，再改期望或实现。不要改成 `CurrentCulture`。

- [ ] **步骤 5：Commit**

```powershell
git add src/ForgeDeck.Core/Files/DirectoryLister.cs tests/ForgeDeck.Core.Tests/DirectoryListerTests.cs
git commit -m @"
test(core): 新增 DirectoryLister 一层列举与路径守卫

终端文件树需要按层列出工作目录，且禁止用 .. 逃出 root。
新增 Files/DirectoryLister：目录在前、点文件保留、扩展名小写；越界 validation、缺失 not_found。
"@
```

（Windows PowerShell 用 here-string；标题 + 空行 + 正文，符合仓库提交规范。）

---

### 任务 2：桥方法 `fs.list`

**文件：**
- 修改：`tests/ForgeDeck.Core.Tests/BridgeTests.cs`（文件末尾、`Workdirs_AddAndList` 之后追加）
- 修改：`src/ForgeDeck.Core/Bridge/ForgeDeckBridge.cs`

- [ ] **步骤 1：编写失败的桥测试**

在 `BridgeTests` 末尾、最后一个测试之后追加：

```csharp
    [Fact]
    public async Task FsList_ListsTempDirectory()
    {
        var sub = Path.Combine(_dir, "src");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_dir, "a.ts"), "");
        var json = _dir.Replace("\\", "\\\\");
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":80,"method":"fs.list","params":{"path":"{{{json}}}","root":"{{{json}}}"}}""");
        Assert.Null(ErrorOf(resp!));
        var names = ResultOf(resp!).GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
        Assert.Contains("src", names);
        Assert.Contains("a.ts", names);
        Assert.Contains("config.json", names);
        var ts = ResultOf(resp!).GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "a.ts");
        Assert.False(ts.GetProperty("isDirectory").GetBoolean());
        Assert.Equal("ts", ts.GetProperty("extension").GetString());
    }

    [Fact]
    public async Task FsList_MissingParams_ReturnsValidationNotThrow()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":81,"method":"fs.list"}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
        Assert.Equal("路径不能为空", ErrorOf(resp!)!.Value.Message);
    }

    [Fact]
    public async Task FsList_EscapingRoot_ReturnsValidation()
    {
        var rootJson = _dir.Replace("\\", "\\\\");
        var parentJson = Path.GetFullPath(Path.Combine(_dir, "..")).Replace("\\", "\\\\");
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":82,"method":"fs.list","params":{"path":"{{{parentJson}}}","root":"{{{rootJson}}}"}}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
        Assert.Equal("路径超出工作目录", ErrorOf(resp!)!.Value.Message);
    }
```

`_dir` 是 ConfigStore 目录，Load 后根上有 `config.json`。

- [ ] **步骤 2：运行确认失败**

```powershell
dotnet test --filter "FullyQualifiedName~BridgeTests.FsList"
```

预期：`未知方法：fs.list`（code `-32601`）。

- [ ] **步骤 3：注册 `fs.list`**

`ForgeDeckBridge.cs` 增加 `using ForgeDeck.Core.Files;`。

在 `RegisterMethods` 里 `launch.external` **之前**插入（窗口/系统对话框方法仍只在 App 层）：

```csharp
        Dispatcher.Register("fs.list", async p =>
        {
            var path = "";
            var root = "";
            if (p is JsonElement pe)
            {
                if (pe.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                    path = pathEl.GetString() ?? "";
                if (pe.TryGetProperty("root", out var rootEl) && rootEl.ValueKind == JsonValueKind.String)
                    root = rootEl.GetString() ?? "";
            }
            return await Task.Run(() => DirectoryLister.List(path, root));
        });
```

用 `TryGetProperty`：缺 `params` 时 `path`/`root` 为空 → `validation`，不会变成 `internal`。枚举在 `Task.Run` 中，不堵 UI 线程。

头注释「唯一例外 tools.rescan」改为同时提到 `fs.list` 也 `Task.Run`（只读磁盘，不碰 ConfigStore）。

- [ ] **步骤 4：运行测试通过**

```powershell
dotnet test --filter "FullyQualifiedName~FsList|FullyQualifiedName~DirectoryListerTests"
```

再跑全量：

```powershell
dotnet test
```

- [ ] **步骤 5：Commit**

```powershell
git add src/ForgeDeck.Core/Bridge/ForgeDeckBridge.cs tests/ForgeDeck.Core.Tests/BridgeTests.cs
git commit -m @"
feat(bridge): 新增 fs.list 按层列出工作目录

前端文件树每次只请求一层。桥方法校验 path 必须在 root 下，枚举放到线程池，缺参与越界返回 validation 封包。
"@
```

---

### 任务 3：类型与 MockBridge

**文件：**
- 修改：`ui/src/types.ts`
- 修改：`ui/src/bridge.ts`

前端无单测。本任务交付物：`npm run build` 通过，且 Mock 的 `fs.list` 与真实会话 `workdir`（`C:\Projects\atlas-web`）对齐。`fileIcons.ts` 放到任务 4，避免 `noUnusedLocals` 编译失败。

- [ ] **步骤 1：`types.ts` 追加**

```ts
export interface FsEntry {
  name: string;
  path: string;
  isDirectory: boolean;
  extension: string;
}

export interface FsListResult {
  path: string;
  entries: FsEntry[];
}
```

- [ ] **步骤 2：MockBridge 实现 `fs.list`**

`bridge.ts` 顶部 import 增加 `FsEntry, FsListResult`。

在 `MockBridge` 类里加常量和辅助（`handle` 之前）：

```ts
  private static readonly mockTree: Record<string, FsEntry[]> = {
    'C:\\Projects\\atlas-web': [
      e('src', 'C:\\Projects\\atlas-web\\src', true, ''),
      e('node_modules', 'C:\\Projects\\atlas-web\\node_modules', true, ''),
      e('go.mod', 'C:\\Projects\\atlas-web\\go.mod', false, ''),
      e('pom.xml', 'C:\\Projects\\atlas-web\\pom.xml', false, 'xml'),
      e('Program.cs', 'C:\\Projects\\atlas-web\\Program.cs', false, 'cs'),
      e('package.json', 'C:\\Projects\\atlas-web\\package.json', false, 'json'),
      e('README.md', 'C:\\Projects\\atlas-web\\README.md', false, 'md'),
      e('.gitignore', 'C:\\Projects\\atlas-web\\.gitignore', false, ''),
    ],
    'C:\\Projects\\atlas-web\\src': [
      e('App.tsx', 'C:\\Projects\\atlas-web\\src\\App.tsx', false, 'tsx'),
      e('main.ts', 'C:\\Projects\\atlas-web\\src\\main.ts', false, 'ts'),
      e('app.css', 'C:\\Projects\\atlas-web\\src\\app.css', false, 'css'),
      e('Main.java', 'C:\\Projects\\atlas-web\\src\\Main.java', false, 'java'),
      e('app.go', 'C:\\Projects\\atlas-web\\src\\app.go', false, 'go'),
    ],
    'C:\\Projects\\atlas-web\\node_modules': [
      e('left-pad', 'C:\\Projects\\atlas-web\\node_modules\\left-pad', true, ''),
    ],
  };
```

文件顶部（`MockBridge` 类外）加：

```ts
function e(name: string, path: string, isDirectory: boolean, extension: string): FsEntry {
  return { name, path, isDirectory, extension };
}
```

`handle` 的 `switch` 在 `sessions.list` 之前加：

```ts
      case 'fs.list': {
        const root = String(p?.root ?? '');
        const path = String(p?.path ?? '');
        if (!root.trim() || !path.trim()) throw new Error('validation: 路径不能为空');
        const prefix = root.endsWith('\\') ? root : root + '\\';
        const under = path.toLowerCase() === root.toLowerCase()
          || path.toLowerCase().startsWith(prefix.toLowerCase());
        if (!under) throw new Error('validation: 路径超出工作目录');
        const entries = MockBridge.mockTree[path];
        if (!entries) throw new Error('not_found: 目录不存在');
        return { path, entries } satisfies FsListResult;
      }
```

现有 `terminal.create` / `createShell` 已把 `workdir` 写成 `'C:\\Projects\\atlas-web'`，不要改，否则树会对空。

- [ ] **步骤 3：构建确认**

```powershell
cd ui; npm run build
```

预期：`tsc -b` 通过。本任务不创建 `fileIcons.ts`（`noUnusedLocals` 为 true）。

- [ ] **步骤 4：Commit**

```powershell
git add ui/src/types.ts ui/src/bridge.ts
git commit -m @"
feat(ui): MockBridge 增加 fs.list 与文件树类型

纯浏览器开发要与真桥行为一致。新增 FsEntry/FsListResult，Mock 按 C:\Projects\atlas-web 返回含 Java/C#/Go 与 node_modules 的样例树。
"@
```

---

### 任务 4：文件树面板、会话页布局、图标

**文件：**
- 创建：`ui/src/fileIcons.ts`
- 创建：`ui/src/FileTreePanel.tsx`
- 修改：`ui/src/TerminalPanel.tsx`
- 修改：`ui/src/App.tsx`
- 修改：`ui/src/app.css`

- [ ] **步骤 1：创建 `ui/src/fileIcons.ts`**

```ts
export type FileBadge =
  | { kind: 'folder' }
  | { kind: 'file'; label: string; bg: string; fg: string };

const NEUTRAL: Omit<Extract<FileBadge, { kind: 'file' }>, 'kind'> = { label: '•', bg: '#64748b', fg: '#fff' };

export function fileBadge(name: string, isDirectory: boolean, extension: string): FileBadge {
  if (isDirectory) return { kind: 'folder' };
  const base = name.toLowerCase();
  const ext = extension.toLowerCase();
  const named = byFileName(base);
  if (named) return named;
  const mapped = byExtension(ext);
  if (mapped) return mapped;
  if (ext) return { kind: 'file', label: ext.slice(0, 3).toUpperCase(), bg: '#64748b', fg: '#fff' };
  return { kind: 'file', ...NEUTRAL };
}

function file(label: string, bg: string, fg: string): FileBadge {
  return { kind: 'file', label, bg, fg };
}

function byFileName(base: string): FileBadge | null {
  if (base === '.gitignore') return file('GI', '#64748b', '#fff');
  if (base === 'dockerfile' || base.startsWith('dockerfile.')) return file('DK', '#64748b', '#e2e8f0');
  if (base === '.env' || base.startsWith('.env.')) return file('ENV', '#4d7c0f', '#fff');
  if (base === 'go.mod' || base === 'go.sum') return file('GO', '#0891b2', '#fff');
  if (base === 'pom.xml' || base === 'build.gradle' || base === 'build.gradle.kts') return file('JA', '#ea580c', '#fff');
  return null;
}

function byExtension(ext: string): FileBadge | null {
  switch (ext) {
    case 'ts': case 'tsx': return file('TS', '#3b82f6', '#fff');
    case 'js': case 'jsx': case 'mjs': case 'cjs': return file('JS', '#ca8a04', '#1a1608');
    case 'py': return file('PY', '#eab308', '#1a1608');
    case 'cs': case 'csproj': case 'sln': return file('CS', '#8b5cf6', '#fff');
    case 'java': return file('JA', '#ea580c', '#fff');
    case 'go': return file('GO', '#0891b2', '#fff');
    case 'css': case 'scss': case 'less': return file('CSS', '#c084fc', '#1a1224');
    case 'html': case 'htm': return file('HTM', '#f97316', '#1a0f08');
    case 'json': return file('JSON', '#ca8a04', '#1a1608');
    case 'md': case 'markdown': return file('MD', '#64748b', '#fff');
    case 'ps1': return file('PS', '#3178c6', '#fff');
    case 'png': case 'jpg': case 'jpeg': case 'gif': case 'webp': case 'ico': case 'svg':
      return file('IMG', '#16a34a', '#fff');
    default: return null;
  }
}
```

- [ ] **步骤 2：创建 `FileTreePanel.tsx`**

```tsx
import { useCallback, useEffect, useState } from 'react';
import { bridge } from './bridge';
import { fileBadge } from './fileIcons';
import type { FsEntry, FsListResult } from './types';

type Layer =
  | { kind: 'loading' }
  | { kind: 'empty' }
  | { kind: 'entries'; items: FsEntry[] }
  | { kind: 'error'; missing: boolean };

function folderName(root: string) {
  const trimmed = root.replace(/[\\/]+$/, '');
  const parts = trimmed.split(/[\\/]/);
  return parts[parts.length - 1] || trimmed;
}

function isMissingError(msg: string) {
  return msg.includes('not_found') || msg.includes('目录不存在');
}

export function FileTreePanel({ root, onError }: { root: string | null; onError: (msg: string) => void }) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [layers, setLayers] = useState<Map<string, Layer>>(() => new Map());

  const load = useCallback(async (path: string, rootPath: string, keepOnFail: boolean) => {
    setLayers((prev) => {
      const cur = prev.get(path);
      if (keepOnFail && cur?.kind === 'entries') return prev;
      const next = new Map(prev);
      next.set(path, { kind: 'loading' });
      return next;
    });
    try {
      const r = await bridge.request<FsListResult>('fs.list', { path, root: rootPath });
      setLayers((prev) => {
        const next = new Map(prev);
        next.set(path, r.entries.length === 0 ? { kind: 'empty' } : { kind: 'entries', items: r.entries });
        return next;
      });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      if (keepOnFail) onError(msg);
      setLayers((prev) => {
        const cur = prev.get(path);
        if (keepOnFail && cur?.kind === 'entries') return prev;
        const next = new Map(prev);
        next.set(path, { kind: 'error', missing: isMissingError(msg) });
        return next;
      });
    }
  }, [onError]);

  useEffect(() => {
    setExpanded(new Set());
    setLayers(new Map());
    if (root) void load(root, root, false);
  }, [root, load]);

  const toggle = (entry: FsEntry) => {
    if (!root || !entry.isDirectory) return;
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(entry.path)) {
        next.delete(entry.path);
        return next;
      }
      next.add(entry.path);
      return next;
    });
    setLayers((prev) => {
      if (!root || prev.has(entry.path)) return prev;
      void load(entry.path, root, false);
      return prev;
    });
  };

  const refresh = () => {
    if (!root) return;
    void load(root, root, true);
    for (const p of expanded) void load(p, root, true);
  };

  return (
    <aside className="file-tree">
      <div className="file-tree-head">
        <span className="file-tree-label">工作区</span>
        {root && <span className="file-tree-name" title={root}>{folderName(root)}</span>}
        {root && (
          <button type="button" className="icon-btn" title="刷新" onClick={refresh}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
              <path d="M21 12a9 9 0 1 1-2.6-6.3M21 3v6h-6" />
            </svg>
          </button>
        )}
      </div>
      <div className="file-tree-body">
        {!root && <div className="file-tree-msg">还没有会话</div>}
        {root && <LayerView layer={layers.get(root)} depth={0} expanded={expanded} layers={layers} onToggle={toggle} />}
      </div>
    </aside>
  );
}

function LayerView({ layer, depth, expanded, layers, onToggle }: {
  layer: Layer | undefined; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; onToggle: (e: FsEntry) => void;
}) {
  if (!layer || layer.kind === 'loading') return <div className="file-tree-msg">读取中…</div>;
  if (layer.kind === 'empty') return <div className="file-tree-msg">空目录</div>;
  if (layer.kind === 'error') return <div className="file-tree-msg">{layer.missing ? '目录不存在' : '无法读取'}</div>;
  return (
    <>
      {layer.items.map((entry) => (
        <TreeNode key={entry.path} entry={entry} depth={depth} expanded={expanded} layers={layers} onToggle={onToggle} />
      ))}
    </>
  );
}

function TreeNode({ entry, depth, expanded, layers, onToggle }: {
  entry: FsEntry; depth: number; expanded: Set<string>;
  layers: Map<string, Layer>; onToggle: (e: FsEntry) => void;
}) {
  const open = entry.isDirectory && expanded.has(entry.path);
  const badge = fileBadge(entry.name, entry.isDirectory, entry.extension);
  return (
    <>
      <div
        className={`file-tree-row${entry.isDirectory ? ' dir' : ''}`}
        style={{ paddingLeft: 8 + depth * 12 }}
        onClick={() => onToggle(entry)}
      >
        <span className={`file-chevron${open ? ' open' : ''}${entry.isDirectory ? '' : ' hidden'}`}>▸</span>
        {badge.kind === 'folder' ? (
          <svg className="file-folder" viewBox="0 0 16 16" fill="none" aria-hidden>
            <path d="M2 4.2h4.1L7.4 5.8H14V13H2z" stroke="currentColor" strokeWidth="1.3" />
            <path d="M2 4.2V3h4.1L7 4.2" stroke="currentColor" strokeWidth="1.3" />
          </svg>
        ) : (
          <span className="file-badge" style={{ background: badge.bg, color: badge.fg }}>{badge.label}</span>
        )}
        <span className="file-tree-item">{entry.name}</span>
      </div>
      {open && (
        <LayerView layer={layers.get(entry.path)} depth={depth + 1} expanded={expanded} layers={layers} onToggle={onToggle} />
      )}
    </>
  );
}
```

文件行点了会进 `onToggle`，函数开头对非目录 `return`，无选中态。

- [ ] **步骤 3：改 `TerminalPanel`**

props 增加 `workdir: string | null`、`onError: (msg: string) => void`。文件顶部 import `FileTreePanel`。

`return` 改为：

```tsx
  return (
    <section className="terminal">
      <FileTreePanel root={workdir} onError={onError} />
      <div className="term-main">
        <div className="term-tabs" id="termTabs">
          {/* 现有 tabs 与新建按钮，不要改逻辑 */}
        </div>
        {sessions.length === 0 && (
          <div className="term-empty">
            <p>还没有会话。从快速启动打开工具，或新建空白会话。</p>
            <button className="btn" onClick={onNewSession}>新建空白会话</button>
          </div>
        )}
        {sessions.map((s) => (
          <div key={s.sessionId}
            ref={(el) => { if (el) containers.current.set(s.sessionId, el); else containers.current.delete(s.sessionId); }}
            className="term-body" style={{ display: s.sessionId === activeId ? 'block' : 'none' }} />
        ))}
      </div>
    </section>
  );
```

把现有 `term-tabs` / empty / `term-body` 原样搬进 `term-main`，不要重写 xterm effect。

- [ ] **步骤 4：改 `App.tsx`**

`termStage` 已存在。找到：

```tsx
          <TerminalPanel visible={termStage} sessions={sessions} activeId={activeSessionId}
            onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
```

改为：

```tsx
          <TerminalPanel visible={termStage} sessions={sessions} activeId={activeSessionId}
            workdir={sessions.find((s) => s.sessionId === activeSessionId)?.workdir ?? null}
            onError={(msg) => toast(msg, 'error')}
            onActivate={setActiveSessionId} onNewSession={handleNewShell} onCloseSession={handleCloseSession} />
```

根节点 `className={\`app${termStage ? ' term-stage' : ''}\`}` 已有，不必改。收窄 rail 只靠 CSS。

- [ ] **步骤 5：改 `app.css`**

在产品补充区 `.app.term-stage .main` 规则旁追加（不要改 `@media (max-width:920px)` 那一段）：

```css
.app.term-stage { --rail: 64px; }
.app.term-stage .rail { padding: 18px 8px; }
.app.term-stage .brand { padding: 0; justify-content: center; }
.app.term-stage .brand>div:not(.brand-mark),
.app.term-stage .nav-label,
.app.term-stage .nav button span,
.app.term-stage .rail-foot { display: none; }
.app.term-stage .nav button { justify-content: center; padding: 11px; }

.terminal { display: grid; grid-template-columns: 220px 1fr; grid-template-rows: minmax(0,1fr); }
.term-main { display: grid; grid-template-rows: 38px 1fr; min-width: 0; min-height: 0; }
.file-tree {
  min-width: 0; min-height: 0; display: flex; flex-direction: column;
  border-right: 1px solid var(--border);
  background: color-mix(in oklch, var(--surface) 40%, oklch(13% .02 170));
}
.file-tree-head {
  height: 38px; padding: 0 8px 0 12px; border-bottom: 1px solid var(--border);
  display: flex; align-items: center; gap: 8px;
}
.file-tree-label { color: var(--muted); font: 10px var(--font-mono); letter-spacing: .1em; text-transform: uppercase; }
.file-tree-name { flex: 1; min-width: 0; color: var(--fg); font: 11px var(--font-mono); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.file-tree-head .icon-btn { width: 28px; height: 28px; flex-shrink: 0; }
.file-tree-body { flex: 1; overflow: auto; padding: 6px 0 10px; }
.file-tree-msg { color: var(--muted); font-size: 11px; padding: 10px 12px; }
.file-tree-row {
  display: flex; align-items: center; gap: 6px; padding: 3px 10px 3px 0;
  font: 12px/1.45 var(--font-mono); color: var(--fg); white-space: nowrap; user-select: none;
}
.file-tree-row.dir { cursor: pointer; }
.file-tree-row:hover { background: var(--fg-soft); }
.file-tree-item { overflow: hidden; text-overflow: ellipsis; }
.file-chevron { width: 10px; color: var(--muted); font-size: 10px; flex-shrink: 0; display: inline-block; }
.file-chevron.open { transform: rotate(90deg); }
.file-chevron.hidden { visibility: hidden; }
.file-folder { width: 14px; height: 14px; flex-shrink: 0; color: var(--accent); }
.file-badge {
  width: 14px; height: 14px; border-radius: 3px; flex-shrink: 0;
  display: grid; place-items: center; font: 700 7px/1 var(--font-mono);
}
```

后写的 `.terminal { display:grid; grid-template-columns: 220px 1fr; ...}` 覆盖设计稿里 `grid-template-rows: 38px 1fr`（标签已进 `term-main`）。

- [ ] **步骤 6：构建**

```powershell
cd ui; npm run build
npm run lint
dotnet test
```

预期：前端 build / lint 通过；后端测试仍绿。

- [ ] **步骤 7：手动核对（`npm run dev` 即可，Mock 已覆盖树）**

1. 浏览器打开 Vite：进「终端会话」或新建会话。左侧导航应收成图标，仍能点回快速启动。
2. 文件树顶栏为「工作区 / atlas-web」，第一层可见 `src`、`node_modules`、`Program.cs`、`go.mod`、`pom.xml` 等。
3. `node_modules` 默认折叠；点开才出现 `left-pad`。`src` 下能看到 `Main.java`（JA 橙红）、`app.go`（GO 青）、`App.tsx`（TS 蓝）；根上 `Program.cs` 为 CS 紫。
4. 点文件无反应；点刷新不崩。
5. 切回快速启动：导航恢复宽栏，文件树消失。
6. 有桌面壳时再用 `$env:FORGEDECK_DEV='1'; dotnet run --project src/ForgeDeck.App` 对真实目录走一遍（权限不足的文件夹应显示「无法读取」）。

- [ ] **步骤 8：Commit**

```powershell
git add ui/src/fileIcons.ts ui/src/FileTreePanel.tsx ui/src/TerminalPanel.tsx ui/src/App.tsx ui/src/app.css
git commit -m @"
feat(ui): 终端会话页增加工作目录文件树

进入会话后导航收成图标栏，左侧按层展示启动工作目录，常见代码文件用字母色块（含 Java/C#/Go）。
树跟随激活会话 workdir，不跟踪 cd；点文件无动作。
"@
```

---

## 规格覆盖核对

| 规格章节 | 任务 |
|---|---|
| §3.1 图标栏 + 两列布局 | 4 |
| §3.2 树 UI / 折叠 / hover | 4 |
| §3.3 启动 workdir、不跟随 cd | 4（只用 `session.workdir`） |
| §4 `fs.list` 契约与错误码 | 1–2 |
| §5 DirectoryLister | 1 |
| §6.2 图标表（含 JA/CS/GO、Dockerfile、.env） | 4（`fileIcons.ts`） |
| §6.3 Mock 样例树 | 3 |
| §7 数据流 / 刷新 | 4 |
| §8 测试 | 1–2；前端 4 的 build + 手动 |
| 非目标（监视、打开文件、拖宽、Git） | 不实现 |

类型名全程：`FsEntry`、`FsListResult`、`DirectoryLister.List`、`fs.list`、`FileTreePanel`、`fileBadge`。
