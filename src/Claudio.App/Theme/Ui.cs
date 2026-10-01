using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Claudio.App;

/// <summary>
/// The few building blocks every view repeats, as Claudy's views lean on <c>Theme</c> and
/// <c>microLabel</c>: the primary ink at an opacity, text in Claudy's sizes and rounded face, a
/// capsule, a pill, a hairline.
/// </summary>
internal static class Ui
{
    /// <summary>Set from the card's actual theme; every brush follows it.</summary>
    public static bool IsDark { get; set; } = true;

    /// <summary>Device pixels per unit on the card's screen, for the images drawn to the pixel (shadows, glows).</summary>
    public static double Scale { get; set; } = 1;

    /// <summary>
    /// Claudy's face is SF Pro Rounded, which only Apple's systems may carry. Nunito, shipped with
    /// Claudio under the SIL Open Font License, is the rounded sans closest to it, and its figures
    /// are all one width: the value does not jitter on every refresh, as Claudy's monospaced digits.
    /// </summary>
    public static FontFamily Font { get; } = new("ms-appx:///Assets/Fonts/Nunito.ttf#Nunito");

    public static FontFamily Icons { get; } = new("Segoe Fluent Icons");

    public static Windows.UI.Color Color(Rgba colour, double opacity = 1) =>
        Windows.UI.Color.FromArgb((byte)Math.Round(colour.A * Math.Clamp(opacity, 0, 1)), colour.R, colour.G, colour.B);

    public static SolidColorBrush Brush(Rgba colour, double opacity = 1) => new(Color(colour, opacity));

    /// <summary>SwiftUI's <c>.primary</c>: white on dark glass, black on light.</summary>
    public static Rgba PrimaryInk => IsDark ? new Rgba(255, 255, 255) : new Rgba(0, 0, 0);

    public static SolidColorBrush Primary(double opacity) => Brush(PrimaryInk, opacity);

    public static Rgba White { get; } = new(255, 255, 255);

    /// <summary>
    /// The system's accent, which macOS gives Claudy's borderless buttons ("Sign out", "Floating
    /// widget"); Windows has its own, and the same buttons take it.
    /// </summary>
    public static Rgba SystemAccent
    {
        get
        {
            var colour = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                IsDark ? Windows.UI.ViewManagement.UIColorType.AccentLight2 : Windows.UI.ViewManagement.UIColorType.AccentDark1);
            return new Rgba(colour.R, colour.G, colour.B);
        }
    }

    public static TextBlock Text(string text, double size, FontWeight weight, double opacity = 1, Rgba? colour = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = weight,
            Foreground = colour is { } tint ? Brush(tint, opacity) : Primary(opacity),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        Style(block, size);
        return block;
    }

    /// <summary>
    /// Claudy's type: the rounded face at its size, and SF's tight line height rather than Nunito's
    /// generous one, so a block of text takes the height it takes in Claudy.
    /// </summary>
    public static void Style(TextBlock block, double size)
    {
        block.FontFamily = Font;
        block.FontSize = size;
        block.IsTextScaleFactorEnabled = false;
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        block.LineHeight = Math.Ceiling(size * 1.22);
        block.OpticalMarginAlignment = OpticalMarginAlignment.TrimSideBearings;
        block.TextLineBounds = TextLineBounds.Full;
    }

    /// <summary>Claudy's <c>microLabel</c>: quiet uppercase, slightly tracked, for titles and column headers.</summary>
    public static TextBlock Micro(string text, double opacity)
    {
        var label = Text(text.ToUpperInvariant(), 9.5, FontWeights.SemiBold, opacity);
        label.CharacterSpacing = 95; // 0.9 pt at 9.5 pt
        return label;
    }

    public static void Restyle(TextBlock block, string text, double opacity, Rgba? colour = null)
    {
        block.Text = text;
        block.Foreground = colour is { } tint ? Brush(tint, opacity) : Primary(opacity);
    }

    /// <summary>
    /// A figure and its "%" on one baseline, as Claudy's <c>HStack(alignment: .firstTextBaseline)</c>:
    /// two runs of one text, so the sign sits exactly where the digits stand.
    /// </summary>
    public static void Figure(TextBlock block, string value, Brush ink, bool showsSign, double signSize, Brush signInk, FontWeight signWeight)
    {
        block.Inlines.Clear();
        block.Inlines.Add(new Run { Text = value, Foreground = ink });
        if (showsSign)
        {
            // SwiftUI's spacing of one or two points between the digits and the sign.
            block.Inlines.Add(new Run { Text = "\u200A%", FontSize = signSize, Foreground = signInk, FontWeight = signWeight });
        }
    }

    public static FontIcon Icon(string glyph, double size, double opacity = 1, Rgba? colour = null) => new()
    {
        Glyph = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = colour is { } tint ? Brush(tint, opacity) : Primary(opacity),
        IsTextScaleFactorEnabled = false,
    };

    /// <summary>
    /// A true capsule: SwiftUI's <c>Capsule()</c> rounds the short side fully. A radius larger than
    /// half the height draws an ellipse in WinUI, so the radius follows the element's height.
    /// </summary>
    public static T Capsule<T>(T element) where T : FrameworkElement
    {
        void Round()
        {
            var radius = new CornerRadius(Math.Min(element.ActualHeight, element.ActualWidth) / 2);
            switch (element)
            {
                case Border border:
                    border.CornerRadius = radius;
                    break;
                case Panel panel:
                    SetPanelCorner(panel, radius);
                    break;
                case Control control:
                    control.CornerRadius = radius;
                    break;
                default:
                    break;
            }
        }
        element.SizeChanged += (_, _) => Round();
        if (element is FrameworkElement { Height: > 0 and < double.PositiveInfinity } sized)
        {
            var radius = new CornerRadius(sized.Height / 2);
            switch (element)
            {
                case Border border:
                    border.CornerRadius = radius;
                    break;
                case Panel panel:
                    SetPanelCorner(panel, radius);
                    break;
                default:
                    break;
            }
        }
        return element;
    }

    private static void SetPanelCorner(Panel panel, CornerRadius radius)
    {
        switch (panel)
        {
            case Grid grid:
                grid.CornerRadius = radius;
                break;
            case StackPanel stack:
                stack.CornerRadius = radius;
                break;
            default:
                break;
        }
    }

    /// <summary>A one-pixel rule between sections.</summary>
    public static Border Hairline(double opacity = 0.07) => new() { Height = 1, Background = Primary(opacity) };

    /// <summary>Claudy's header pill: the active model, the origin badge, the error.</summary>
    public static Border Pill(string text, Rgba? tint, double size = 9.5, bool stroked = true)
    {
        var label = Text(text, size, FontWeights.SemiBold, tint is null ? 0.6 : 1, tint);
        return Capsule(new Border
        {
            Child = label,
            Padding = new Thickness(7, 3, 7, 3),
            Background = tint is { } colour ? Brush(colour, 0.16) : Primary(0.08),
            BorderBrush = stroked ? Brush(White, 0.09) : null,
            BorderThickness = new Thickness(stroked ? 1 : 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    public static Microsoft.UI.Xaml.Shapes.Ellipse Dot(double size, Rgba colour, double opacity = 1) => new()
    {
        Width = size,
        Height = size,
        Fill = Brush(colour, opacity),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children)
        {
            row.Children.Add(child);
        }
        return row;
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }
        return column;
    }

    /// <summary>A grid of one row: fixed parts at both ends, the middle column stretching like a SwiftUI <c>Spacer</c>.</summary>
    public static Grid Spread(FrameworkElement leading, FrameworkElement trailing, VerticalAlignment alignment = VerticalAlignment.Center,
                              double minimumGap = 0)
    {
        var grid = new Grid { ColumnSpacing = minimumGap };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        leading.VerticalAlignment = alignment;
        trailing.VerticalAlignment = alignment;
        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(leading);
        grid.Children.Add(trailing);
        return grid;
    }

    /// <summary>A button with nothing but its content: the plain style Claudy gives its controls.</summary>
    public static Button Plain(UIElement content, Action action, string? tooltip = null)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
        {
            button.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        button.PointerEntered += (_, _) => button.Opacity = 0.8;
        button.PointerExited += (_, _) => button.Opacity = 1;
        button.Click += (_, _) => action();
        if (tooltip is not null)
        {
            ToolTipService.SetToolTip(button, tooltip);
        }
        // What a screen reader announces, as Claudy labels its controls for VoiceOver.
        Name(button, content is TextBlock label && label.Text.Length > 0 ? label.Text : tooltip);
        return button;
    }

    /// <summary>Makes any element a click target without a button's chrome.</summary>
    public static void OnTap(UIElement element, Action action, string? name = null)
    {
        Name(element, name);
        element.Tapped += (_, args) =>
        {
            args.Handled = true;
            action();
        };
    }

    public static void Tooltip(DependencyObject element, string? text) => ToolTipService.SetToolTip(element, text);

    /// <summary>The name assistive technologies read for an element.</summary>
    public static void Name(DependencyObject element, string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
        }
    }

    public static FontWeight Medium => FontWeights.Medium;
    public static FontWeight SemiBold => FontWeights.SemiBold;
    public static FontWeight Bold => FontWeights.Bold;
    public static FontWeight Regular => FontWeights.Normal;
}
