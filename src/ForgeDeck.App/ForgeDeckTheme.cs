using System.Windows.Media;
using ForgeDeck.Core;
using Microsoft.Win32;

namespace ForgeDeck.App;

/// <summary>与 ui/src/app.css 令牌对应的本机色，供托盘菜单和 WPF 提示框使用。</summary>
internal static class ForgeDeckTheme
{
    public static Color Bg { get; private set; }
    public static Color Surface { get; private set; }
    public static Color Fg { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Border { get; private set; }
    public static Color Accent { get; private set; }
    public static Color Danger { get; private set; } = Color.FromRgb(229, 72, 77);
    public static Color Hover { get; private set; }

    static ForgeDeckTheme() => Apply(ColorMode.Dark, AccentColor.Teal);

    public static ColorMode Resolve(ColorMode mode) =>
        mode == ColorMode.System
            ? (AppsUseLightTheme() ? ColorMode.Light : ColorMode.Dark)
            : mode;

    public static void Apply(ColorMode mode, AccentColor accent)
    {
        var resolved = Resolve(mode);
        if (resolved == ColorMode.Light)
        {
            Bg = Color.FromRgb(243, 246, 244);
            Surface = Color.FromRgb(255, 255, 255);
            Fg = Color.FromRgb(26, 43, 37);
            Muted = Color.FromRgb(92, 111, 104);
            Border = Color.FromRgb(213, 222, 217);
            Hover = Color.FromRgb(232, 238, 234);
        }
        else
        {
            Bg = Color.FromRgb(6, 21, 16);
            Surface = Color.FromRgb(12, 31, 25);
            Fg = Color.FromRgb(224, 239, 233);
            Muted = Color.FromRgb(138, 158, 150);
            Border = Color.FromRgb(35, 56, 48);
            Hover = Color.FromRgb(25, 43, 37);
        }
        Accent = AccentOf(resolved, accent);
    }

    public static Brush Brush(Color c) => new SolidColorBrush(c);

    private static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i != 0;
        }
        catch (System.Security.SecurityException) { return false; }
        catch (System.IO.IOException) { return false; }
    }

    private static Color AccentOf(ColorMode mode, AccentColor accent) => (mode, accent) switch
    {
        (ColorMode.Light, AccentColor.Blue) => Color.FromRgb(43, 98, 196),
        (ColorMode.Light, AccentColor.Violet) => Color.FromRgb(138, 69, 196),
        (ColorMode.Light, AccentColor.Amber) => Color.FromRgb(160, 107, 16),
        (ColorMode.Light, AccentColor.Rose) => Color.FromRgb(196, 60, 74),
        (ColorMode.Light, _) => Color.FromRgb(26, 138, 92),
        (_, AccentColor.Blue) => Color.FromRgb(126, 176, 245),
        (_, AccentColor.Violet) => Color.FromRgb(201, 160, 240),
        (_, AccentColor.Amber) => Color.FromRgb(232, 192, 112),
        (_, AccentColor.Rose) => Color.FromRgb(240, 144, 144),
        _ => Color.FromRgb(89, 211, 140),
    };
}
