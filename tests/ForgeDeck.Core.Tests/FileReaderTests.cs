using System.Text;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Files;

namespace ForgeDeck.Core.Tests;

public class FileReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));

    public FileReaderTests()
    {
        Directory.CreateDirectory(_root);
        // 与生产端一致：GBK(936) 需注册 CodePages provider 才可用（包经 Core 工程传递引用）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteBytes(byte[] bytes)
    {
        var file = Path.Combine(_root, $"f-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(file, bytes);
        return file;
    }

    private string WriteText(string text, Encoding encoding)
    {
        var file = Path.Combine(_root, $"f-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, text, encoding);
        return file;
    }

    [Fact]
    public void Read_Utf8NoBom_ReturnsUtf8Content()
    {
        var file = WriteText("hello 世界", new UTF8Encoding(false));
        var r = FileReader.Read(file, _root);
        Assert.Equal("hello 世界", r.Content);
        Assert.Equal("utf-8", r.Encoding);
        Assert.Equal(new FileInfo(file).Length, r.Size);
    }

    [Fact]
    public void Read_Utf8Bom_StripsBomAndReportsUtf8Bom()
    {
        var file = WriteText("带 BOM 的内容", new UTF8Encoding(true));
        var r = FileReader.Read(file, _root);
        Assert.Equal("带 BOM 的内容", r.Content); // BOM 不得混入正文（否则首行多出 \uFEFF）
        Assert.Equal("utf-8bom", r.Encoding);
    }

    [Fact]
    public void Read_Utf16Le_ReturnsContent()
    {
        var file = WriteText("UTF-16 小端内容", Encoding.Unicode);
        var r = FileReader.Read(file, _root);
        Assert.Equal("UTF-16 小端内容", r.Content);
        Assert.Equal("utf-16le", r.Encoding);
    }

    [Fact]
    public void Read_Utf16Be_ReturnsContent()
    {
        var file = WriteText("UTF-16 大端内容", Encoding.BigEndianUnicode);
        var r = FileReader.Read(file, _root);
        Assert.Equal("UTF-16 大端内容", r.Content);
        Assert.Equal("utf-16be", r.Encoding);
    }

    [Fact]
    public void Read_Gbk_FallsBackToGbkDecoding()
    {
        var file = WriteText("中文日志：构建失败，错误码 42。", Encoding.GetEncoding(936));
        var r = FileReader.Read(file, _root);
        Assert.Equal("中文日志：构建失败，错误码 42。", r.Content);
        Assert.Equal("gbk", r.Encoding);
    }

    [Fact]
    public void Read_PureAscii_IsUtf8()
    {
        var file = WriteText("plain ascii log line", new UTF8Encoding(false));
        var r = FileReader.Read(file, _root);
        Assert.Equal("plain ascii log line", r.Content);
        Assert.Equal("utf-8", r.Encoding);
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmptyContent()
    {
        var file = WriteBytes(Array.Empty<byte>());
        var r = FileReader.Read(file, _root);
        Assert.Equal("", r.Content);
        Assert.Equal("utf-8", r.Encoding);
        Assert.Equal(0, r.Size);
    }

    [Fact]
    public void Read_ExactLimit_Succeeds()
    {
        var file = WriteBytes(Encoding.ASCII.GetBytes(new string('a', (int)FileReader.MaxBytes)));
        var r = FileReader.Read(file, _root);
        Assert.Equal(FileReader.MaxBytes, r.Size);
        Assert.Equal(FileReader.MaxBytes, r.Content.Length);
    }

    [Fact]
    public void Read_OverLimit_ThrowsValidation()
    {
        var file = WriteBytes(Encoding.ASCII.GetBytes(new string('a', (int)FileReader.MaxBytes + 1)));
        var ex = Assert.Throws<BridgeException>(() => FileReader.Read(file, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("文件超过 1MB，请使用系统默认方式打开", ex.Message);
    }

    [Fact]
    public void Read_Binary_ThrowsValidation()
    {
        // MZ 头：PE 可执行文件前几个字节即含 NUL
        var file = WriteBytes([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
        var ex = Assert.Throws<BridgeException>(() => FileReader.Read(file, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("二进制文件，无法以文本查看", ex.Message);
    }

    [Fact]
    public void Read_Directory_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => FileReader.Read(_root, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("只能查看文件", ex.Message);
    }

    [Fact]
    public void Read_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() => FileReader.Read(Path.Combine(_root, "gone.txt"), _root));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("文件不存在", ex.Message);
    }

    [Fact]
    public void Read_OutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => FileReader.Read(parent, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);
    }

    [Fact]
    public void Read_EmptyPath_ThrowsValidation()
    {
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileReader.Read("  ", _root)).Code);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => FileReader.Read(_root, "")).Code);
    }
}
