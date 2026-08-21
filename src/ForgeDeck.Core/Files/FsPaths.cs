using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

internal static class FsPaths
{
    public static (string FullPath, string FullRoot) ResolveUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            throw new BridgeException("validation", "路径不能为空");

        var fullRoot = Normalize(root);
        var fullPath = Normalize(path);
        if (!IsUnderRoot(fullPath, fullRoot))
            throw new BridgeException("validation", "路径超出工作目录");
        return (fullPath, fullRoot);
    }

    public static string Normalize(string p)
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

    public static bool IsUnderRoot(string full, string root) =>
        full.Equals(root, StringComparison.OrdinalIgnoreCase)
        || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
