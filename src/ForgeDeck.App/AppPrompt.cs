using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ForgeDeck.App;

internal enum PromptChoice { Dismiss, Secondary, Primary }

/// <summary>与 Web 端 Modal 同色同结构的本机提示框，替代系统 MessageBox。</summary>
internal static class AppPrompt
{
    public static PromptChoice Ask(
        Window? owner, string title, string message,
        string secondary, string primary, bool primaryDanger = false)
    {
        var dlg = new PromptWindow(title, message, secondary, primary, primaryDanger);
        if (owner != null && owner.IsVisible)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true ? dlg.Choice : PromptChoice.Dismiss;
    }

    public static bool Confirm(Window? owner, string title, string message, string confirm, string cancel, bool danger = true)
        => Ask(owner, title, message, cancel, confirm, danger) == PromptChoice.Primary;

    public static void Alert(Window? owner, string title, string message)
        => Ask(owner, title, message, "", "确定");
}

internal sealed class PromptWindow : Window
{
    public PromptChoice Choice { get; private set; } = PromptChoice.Dismiss;

    public PromptWindow(string title, string message, string secondary, string primary, bool primaryDanger)
    {
        Title = "ForgeDeck";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Fg);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Microsoft YaHei");
        FontSize = 13;
        SnapsToDevicePixels = true;

        var card = new Border
        {
            Background = ForgeDeckTheme.Brush(ForgeDeckTheme.Surface),
            BorderBrush = ForgeDeckTheme.Brush(ForgeDeckTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(12),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.45,
                BlurRadius = 28,
                ShadowDepth = 8,
                Direction = 270,
            },
        };

        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = MakeIconButton();
        close.Click += (_, _) => { Choice = PromptChoice.Dismiss; DialogResult = false; };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Georgia, Times New Roman, Microsoft YaHei UI"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Fg),
        });
        titles.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Muted),
        });
        head.Children.Add(titles);
        root.Children.Add(head);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (!string.IsNullOrEmpty(secondary))
        {
            var sec = MakeButton(secondary, filled: false, danger: false);
            sec.Margin = new Thickness(0, 0, 8, 0);
            sec.Click += (_, _) => { Choice = PromptChoice.Secondary; DialogResult = true; };
            foot.Children.Add(sec);
        }
        var pri = MakeButton(primary, filled: true, danger: primaryDanger);
        pri.Click += (_, _) => { Choice = PromptChoice.Primary; DialogResult = true; };
        foot.Children.Add(pri);
        root.Children.Add(foot);

        card.Child = root;
        Content = card;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Choice = PromptChoice.Dismiss;
            DialogResult = false;
            e.Handled = true;
        };
    }

    private static Button MakeIconButton()
    {
        var btn = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            FontSize = 16,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Foreground = ForgeDeckTheme.Brush(ForgeDeckTheme.Muted),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Template = ButtonChrome(radius: 6, hoverBg: ForgeDeckTheme.Hover, hoverFg: ForgeDeckTheme.Fg),
        };
        return btn;
    }

    private static Button MakeButton(string text, bool filled, bool danger)
    {
        var bg = filled
            ? (danger ? ForgeDeckTheme.Danger : ForgeDeckTheme.Accent)
            : Colors.Transparent;
        var fg = filled
            ? (danger ? Colors.White : ForgeDeckTheme.Bg)
            : ForgeDeckTheme.Fg;
        var border = filled
            ? bg
            : ForgeDeckTheme.Border;
        var hover = filled
            ? (danger ? Color.FromRgb(200, 55, 60) : Color.FromRgb(70, 180, 115))
            : ForgeDeckTheme.Hover;
        return new Button
        {
            Content = text,
            Padding = new Thickness(13, 9, 13, 9),
            FontWeight = filled ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            Background = ForgeDeckTheme.Brush(bg),
            Foreground = ForgeDeckTheme.Brush(fg),
            BorderBrush = ForgeDeckTheme.Brush(border),
            BorderThickness = new Thickness(1),
            Template = ButtonChrome(radius: 6, hoverBg: hover, hoverFg: fg),
        };
    }

    private static ControlTemplate ButtonChrome(double radius, Color hoverBg, Color hoverFg)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, ForgeDeckTheme.Brush(hoverBg)));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, ForgeDeckTheme.Brush(hoverFg)));
        template.Triggers.Add(hover);
        return template;
    }
}
