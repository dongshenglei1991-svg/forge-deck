using System.Text;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Files;

namespace ForgeDeck.Core.Tests;

public class FileWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));

    public FileWriterTests()
    {
        Directory.CreateDirectory(_root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Existing(string name, string text = "old")
    {
        var file = Path.Combine(_root, name);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        return file;
    }

    [Fact]
    public void Write_Utf8_OverwritesAndRoundTrips()
    {
        var file = Existing("a.txt");
        var r = FileWriter.Write(file, _root, "hello 世界", "utf-8");
        var read = FileReader.Read(file, _root);
        Assert.Equal("hello 世界", read.Content);
        Assert.Equal("utf-8", read.Encoding);
        Assert.Equal(read.Size, r.Size);
        Assert.Equal(new FileInfo(file).Length, r.Size);
    }

    [Fact]
    public void Write_Utf8Bom_PreservesBom()
    {
        var file = Existing("bom.txt");
        FileWriter.Write(file, _root, "带 BOM", "utf-8bom");
        var read = FileReader.Read(file, _root);
        Assert.Equal("带 BOM", read.Content);
        Assert.Equal("utf-8bom", read.Encoding);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void Write_Gbk_PreservesEncoding()
    {
        var file = Existing("gbk.log");
        FileWriter.Write(file, _root, "中文日志：构建失败。", "gbk");
        var read = FileReader.Read(file, _root);
        Assert.Equal("中文日志：构建失败。", read.Content);
        Assert.Equal("gbk", read.Encoding);
    }

    [Fact]
    public void Write_Utf16Le_RoundTrips()
    {
        var file = Existing("le.txt");
        FileWriter.Write(file, _root, "UTF-16 小端", "utf-16le");
        var read = FileReader.Read(file, _root);
        Assert.Equal("UTF-16 小端", read.Content);
        Assert.Equal("utf-16le", read.Encoding);
    }

    [Fact]
    public void Write_EmptyContent_Ok()
    {
        var file = Existing("empty.txt", "not empty");
        var r = FileWriter.Write(file, _root, "", "utf-8");
        Assert.Equal(0, r.Size);
        Assert.Equal("", FileReader.Read(file, _root).Content);
    }

    [Fact]
    public void Write_MissingEncoding_DefaultsToUtf8()
    {
        var file = Existing("def.txt");
        FileWriter.Write(file, _root, "ascii", "");
        Assert.Equal("utf-8", FileReader.Read(file, _root).Encoding);
    }

    [Fact]
    public void Write_UnknownEncoding_ThrowsValidation()
    {
        var file = Existing("x.txt");
        var ex = Assert.Throws<BridgeException>(() => FileWriter.Write(file, _root, "x", "latin1"));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("不支持的编码", ex.Message);
    }

    [Fact]
    public void Write_OverLimit_ThrowsValidationAndKeepsOriginal()
    {
        var file = Existing("big.txt", "keep");
        var huge = new string('a', (int)FileReader.MaxBytes + 1);
        var ex = Assert.Throws<BridgeException>(() => FileWriter.Write(file, _root, huge, "utf-8"));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("文件超过 1MB，请使用系统默认方式打开", ex.Message);
        Assert.Equal("keep", File.ReadAllText(file, new UTF8Encoding(false)));
    }

    [Fact]
    public void Write_Directory_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => FileWriter.Write(_root, _root, "x", "utf-8"));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("只能写入文件", ex.Message);
    }

    [Fact]
    public void Write_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() =>
            FileWriter.Write(Path.Combine(_root, "gone.txt"), _root, "x", "utf-8"));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("文件不存在", ex.Message);
    }

    [Fact]
    public void Write_OutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => FileWriter.Write(parent, _root, "x", "utf-8"));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);
    }

    [Fact]
    public void Write_EmptyPath_ThrowsValidation()
    {
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileWriter.Write("  ", _root, "x", "utf-8")).Code);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileWriter.Write(_root, "", "x", "utf-8")).Code);
    }

    [Fact]
    public void Write_DoesNotLeaveTmpFile()
    {
        var file = Existing("atom.txt");
        FileWriter.Write(file, _root, "new", "utf-8");
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.Equal("new", FileReader.Read(file, _root).Content);
    }
}
