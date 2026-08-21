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
