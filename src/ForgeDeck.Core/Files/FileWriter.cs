using System.Text;
using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public sealed record FileWriterResult(long Size);

/// <summary>文本文件写入（fs.write）：路径守卫 + 已有文件覆盖 + 原编码回写 + 1MB 上限 + 同目录临时文件再替换。
/// 编码名与 FileReader 一致（utf-8 / utf-8bom / utf-16le / utf-16be / gbk）；空编码按 utf-8。</summary>
public static class FileWriter
{
    private static readonly Encoding Gbk;

    static FileWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936);
    }

    public static FileWriterResult Write(string path, string root, string content, string encoding)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (Directory.Exists(fullPath)) throw new BridgeException("validation", "只能写入文件");
        if (!File.Exists(fullPath)) throw new BridgeException("not_found", "文件不存在");

        var enc = Resolve(encoding);
        content ??= "";
        var preamble = enc.GetPreamble();
        var body = enc.GetBytes(content);
        long size = preamble.Length + (long)body.Length;
        if (size > FileReader.MaxBytes)
            throw new BridgeException("validation", "文件超过 1MB，请使用系统默认方式打开");

        var tmp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (preamble.Length > 0) fs.Write(preamble, 0, preamble.Length);
                fs.Write(body, 0, body.Length);
            }
            File.Move(tmp, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException("io", "无法写入文件");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 替换已成功或无权清临时文件 */ }
        }

        return new FileWriterResult(size);
    }

    private static Encoding Resolve(string encoding)
    {
        var key = (encoding ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) key = "utf-8";
        return key switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            "gbk" => Gbk,
            _ => throw new BridgeException("validation", "不支持的编码"),
        };
    }
}
