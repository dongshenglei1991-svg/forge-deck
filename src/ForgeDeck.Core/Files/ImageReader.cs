using System.Text;
using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public sealed record ImageReaderResult(string Data, string Mime, long Size);

/// <summary>图片读取（fs.readImage）：路径守卫 + 20MB 上限 + 魔数嗅探，返回 base64 与 MIME 供前端拼 data URL。
/// 格式按文件头识别而非扩展名——扩展名会骗人（png 改名 .jpg），魔数不会；SVG 是唯一文本格式，靠前 512 字节内
/// 出现 "&lt;svg" 判定（&lt;img&gt; 上下文加载 SVG 不执行脚本，无安全顾虑）。嗅探不出已知格式即拒绝，
/// 天然挡掉伪装成图片的二进制。</summary>
public static class ImageReader
{
    public const long MaxBytes = 20 * 1024 * 1024;
    private const int SniffBytes = 512;

    // PNG：8 字节固定签名
    private static readonly byte[] PngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    // JPEG：FF D8 FF（第四字节随子类型变化，只比对前三）
    private static readonly byte[] JpgSig = [0xFF, 0xD8, 0xFF];
    // GIF87a / GIF89a 共有前缀 "GIF8"
    private static readonly byte[] GifSig = [0x47, 0x49, 0x46, 0x38];

    public static ImageReaderResult Read(string path, string root)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (Directory.Exists(fullPath)) throw new BridgeException("validation", "只能查看文件");
        if (!File.Exists(fullPath)) throw new BridgeException("not_found", "文件不存在");

        byte[] bytes;
        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
            if (size > MaxBytes) throw new BridgeException("validation", "图片超过 20MB，请使用系统默认方式打开");
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException("io", "无法读取文件");
        }

        var mime = SniffMime(bytes);
        if (mime is null) throw new BridgeException("validation", "不支持的图片格式");
        return new ImageReaderResult(Convert.ToBase64String(bytes), mime, size);
    }

    private static string? SniffMime(byte[] bytes)
    {
        if (StartsWith(bytes, PngSig)) return "image/png";
        if (StartsWith(bytes, JpgSig)) return "image/jpeg";
        if (StartsWith(bytes, GifSig)) return "image/gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return "image/bmp"; // "BM"
        // WebP：RIFF....WEBP（4 字节长度字段在中间）
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        // ICO：00 00 01 00（保留字 0 + 类型 1 + 数量）
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
            return "image/x-icon";

        return LooksLikeSvg(bytes) ? "image/svg+xml" : null;
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, SniffBytes);
        for (var i = 0; i < limit; i++)
            if (bytes[i] == 0) return false; // 二进制内容不是 SVG
        var head = Encoding.ASCII.GetString(bytes, 0, limit);
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(byte[] bytes, byte[] sig)
    {
        if (bytes.Length < sig.Length) return false;
        for (var i = 0; i < sig.Length; i++)
            if (bytes[i] != sig[i]) return false;
        return true;
    }
}
