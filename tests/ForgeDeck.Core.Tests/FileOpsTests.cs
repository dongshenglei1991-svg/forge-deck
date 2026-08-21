using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Files;

namespace ForgeDeck.Core.Tests;

public class FileOpsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeShellOpener _opener = new();

    public FileOpsTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "App.tsx"), "x");
        File.WriteAllText(Path.Combine(_root, "README.md"), "hi");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static string Full(string p) =>
        Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    [Fact]
    public void Open_File_InvokesOpener()
    {
        var file = Path.Combine(_root, "README.md");
        FileOps.Open(file, _root, _opener);
        Assert.Equal(Full(file), _opener.Files.Single());
        Assert.Empty(_opener.Dirs);
    }

    [Fact]
    public void Open_Directory_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => FileOps.Open(Path.Combine(_root, "src"), _root, _opener));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("只能打开文件", ex.Message);
        Assert.Empty(_opener.Files);
        Assert.Empty(_opener.Dirs);
    }

    [Fact]
    public void Open_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() =>
            FileOps.Open(Path.Combine(_root, "gone.txt"), _root, _opener));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("文件不存在", ex.Message);
    }

    [Fact]
    public void Open_OutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => FileOps.Open(parent, _root, _opener));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);
    }

    [Fact]
    public void Open_EmptyPath_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => FileOps.Open("", _root, _opener));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径不能为空", ex.Message);
    }

    [Fact]
    public void OpenWithSystem_File_InvokesFileOpener()
    {
        var file = Path.Combine(_root, "README.md");
        FileOps.OpenWithSystem(file, _root, _opener);
        Assert.Equal(Full(file), _opener.Files.Single());
        Assert.Empty(_opener.Dirs);
    }

    [Fact]
    public void OpenWithSystem_Directory_InvokesDirectoryOpener()
    {
        var dir = Path.Combine(_root, "src");
        FileOps.OpenWithSystem(dir, _root, _opener);
        Assert.Equal(Full(dir), _opener.Dirs.Single());
        Assert.Empty(_opener.Files);
    }

    [Fact]
    public void OpenWithSystem_Root_IsAllowed()
    {
        FileOps.OpenWithSystem(_root, _root, _opener);
        Assert.Equal(Full(_root), _opener.Dirs.Single());
    }

    [Fact]
    public void OpenWithSystem_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() =>
            FileOps.OpenWithSystem(Path.Combine(_root, "nope"), _root, _opener));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("路径不存在", ex.Message);
    }

    [Fact]
    public void Delete_File_RemovesIt()
    {
        var file = Path.Combine(_root, "README.md");
        FileOps.Delete(file, _root);
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(Path.Combine(_root, "src")));
    }

    [Fact]
    public void Delete_Directory_RemovesRecursively()
    {
        var src = Path.Combine(_root, "src");
        FileOps.Delete(src, _root);
        Assert.False(Directory.Exists(src));
        Assert.False(File.Exists(Path.Combine(src, "App.tsx")));
        Assert.True(File.Exists(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void Delete_Root_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => FileOps.Delete(_root, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("不能删除工作目录根", ex.Message);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void Delete_OutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => FileOps.Delete(parent, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);
    }

    [Fact]
    public void Delete_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() => FileOps.Delete(Path.Combine(_root, "gone.txt"), _root));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("路径不存在", ex.Message);
    }

    [Fact]
    public void Delete_EmptyPath_ThrowsValidation()
    {
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileOps.Delete("  ", _root)).Code);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileOps.Delete(_root, "")).Code);
    }

    private sealed class FakeShellOpener : IShellOpener
    {
        public List<string> Files { get; } = new();
        public List<string> Dirs { get; } = new();
        public void OpenFile(string fullPath) => Files.Add(fullPath);
        public void OpenDirectory(string fullPath) => Dirs.Add(fullPath);
    }
}
