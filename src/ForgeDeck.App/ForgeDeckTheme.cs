using System.Windows.Media;

namespace ForgeDeck.App;

/// <summary>与 ui/src/app.css 令牌对应的本机色，供托盘菜单和 WPF 提示框使用。</summary>
internal static class ForgeDeckTheme
{
    public static readonly Color Bg = Color.FromRgb(6, 21, 16);
    public static readonly Color Surface = Color.FromRgb(12, 31, 25);
    public static readonly Color Fg = Color.FromRgb(224, 239, 233);
    public static readonly Color Muted = Color.FromRgb(138, 158, 150);
    public static readonly Color Border = Color.FromRgb(35, 56, 48);
    public static readonly Color Accent = Color.FromRgb(89, 211, 140);
    public static readonly Color Danger = Color.FromRgb(229, 72, 77);
    public static readonly Color Hover = Color.FromRgb(25, 43, 37);

    public static Brush Brush(Color c) => new SolidColorBrush(c);
}
