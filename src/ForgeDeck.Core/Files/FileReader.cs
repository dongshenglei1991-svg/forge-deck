using System.Text;
using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public sealed record FileReaderResult(string Content, string Encoding, long Size);

/// <summary>文本文件读取（fs.read）：路径守卫 + 1MB 上限 + 二进制嗅探 + 编码检测。
/// 中文 Windows 下日志/配置常为 GBK，无 BOM 时靠"严格 UTF-8 校验"区分两者：
/// GBK 双字节序列绝大多数会破坏 UTF-8 连续性，校验失败即按 GBK 解码（无效字节替换为 '?'，不抛错）。
/// UTF-16 文本对 ASCII 字符天然含 NUL 字节，因此 BOM 判定必须先于二进制嗅探。</summary>
public static class FileReader
{
    public const long MaxBytes = 1024 * 1024;
    private const int SniffBytes = 8192;

    // CodePages provider 进程内注册一次即可。注意：静态字段初始化器先于静态构造器体执行，
    // 因此 Gbk 必须在注册 provider 之后才能赋值（GetEncoding(936) 否则抛 NotSupportedException，
    // 表现为 TypeInitializationException；测试进程因测试类构造器预先注册过 provider 而掩盖过一次）。
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Gbk;

    static FileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936);
    }

    public static FileReaderResult Read(string path, string root)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (Directory.Exists(fullPath)) throw new BridgeException("validation", "只能查看文件");
        if (!File.Exists(fullPath)) throw new BridgeException("not_found", "文件不存在");

        byte[] bytes;
        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
            if (size > MaxBytes) throw new BridgeException("validation", "文件超过 1MB，请使用系统默认方式打开");
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

        var (content, encoding) = Decode(bytes);
        return new FileReaderResult(content, encoding, size);
    }

    private static (string Content, string Encoding) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8bom");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be");

        var sniff = Math.Min(bytes.Length, SniffBytes);
        for (var i = 0; i < sniff; i++)
            if (bytes[i] == 0)
                throw new BridgeException("validation", "二进制文件，无法以文本查看");

        try
        {
            return (Utf8Strict.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return (Gbk.GetString(bytes), "gbk");
        }
    }
}
