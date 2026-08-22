using System.Text;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Files;

namespace ForgeDeck.Core.Tests;

public class ImageReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));

    public ImageReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteBytes(byte[] bytes)
    {
        var file = Path.Combine(_root, $"img-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(file, bytes);
        return file;
    }

    // 各格式的魔数头（嗅探只看头部，不需要完整可解码文件）
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] JpgHead = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] GifHead = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00]; // GIF89a
    private static readonly byte[] BmpHead = [0x42, 0x4D, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] WebpHead = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]; // RIFF....WEBP
    private static readonly byte[] IcoHead = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00];

    [Fact]
    public void Read_Png_ReturnsBase64AndMime()
    {
        var file = WriteBytes(PngHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/png", r.Mime);
        Assert.Equal(PngHead, Convert.FromBase64String(r.Data));
        Assert.Equal(PngHead.Length, r.Size);
    }

    [Fact]
    public void Read_Jpeg_ReturnsBase64AndMime()
    {
        var file = WriteBytes(JpgHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/jpeg", r.Mime);
        Assert.Equal(JpgHead, Convert.FromBase64String(r.Data));
    }

    [Fact]
    public void Read_Gif_ReturnsBase64AndMime()
    {
        var file = WriteBytes(GifHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/gif", r.Mime);
    }

    [Fact]
    public void Read_Bmp_ReturnsBase64AndMime()
    {
        var file = WriteBytes(BmpHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/bmp", r.Mime);
    }

    [Fact]
    public void Read_Webp_ReturnsBase64AndMime()
    {
        var file = WriteBytes(WebpHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/webp", r.Mime);
    }

    [Fact]
    public void Read_Ico_ReturnsBase64AndMime()
    {
        var file = WriteBytes(IcoHead);
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/x-icon", r.Mime);
    }

    [Fact]
    public void Read_SvgText_ReturnsSvgMime()
    {
        var svg = "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>"u8;
        var file = WriteBytes(svg.ToArray());
        var r = ImageReader.Read(file, _root);
        Assert.Equal("image/svg+xml", r.Mime);
    }

    [Fact]
    public void Read_PlainText_ThrowsValidation()
    {
        var file = WriteBytes("hello world, just a text file"u8.ToArray());
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(file, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("不支持的图片格式", ex.Message);
    }

    [Fact]
    public void Read_TruncatedSignature_ThrowsValidation()
    {
        var file = WriteBytes([0x89, 0x50, 0x4E]); // PNG 头被截断
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(file, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("不支持的图片格式", ex.Message);
    }

    [Fact]
    public void Read_OverLimit_ThrowsValidation()
    {
        var bytes = new byte[ImageReader.MaxBytes + 1];
        PngHead.CopyTo(bytes, 0);
        var file = WriteBytes(bytes);
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(file, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("图片超过 20MB，请使用系统默认方式打开", ex.Message);
    }

    [Fact]
    public void Read_Directory_ThrowsValidation()
    {
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(_root, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("只能查看文件", ex.Message);
    }

    [Fact]
    public void Read_Missing_ThrowsNotFound()
    {
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(Path.Combine(_root, "gone.png"), _root));
        Assert.Equal("not_found", ex.Code);
        Assert.Equal("文件不存在", ex.Message);
    }

    [Fact]
    public void Read_OutsideRoot_ThrowsValidation()
    {
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        var ex = Assert.Throws<BridgeException>(() => ImageReader.Read(parent, _root));
        Assert.Equal("validation", ex.Code);
        Assert.Equal("路径超出工作目录", ex.Message);
    }

    [Fact]
    public void Read_EmptyPath_ThrowsValidation()
    {
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => ImageReader.Read("  ", _root)).Code);
        Assert.Equal("validation", Assert.Throws<BridgeException>(() => ImageReader.Read(_root, "")).Code);
    }
}
