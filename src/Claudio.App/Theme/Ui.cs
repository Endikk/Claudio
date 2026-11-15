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

    /// <summary>SwiftUI's <c>.secondary</c> and <c>.tertiary</c>: with the primary ink, the three tones text takes.</summary>
    public const double SecondaryOpacity = 0.55;
    public const double TertiaryOpacity = 0.3;

    public static SolidColorBrush Secondary => Primary(SecondaryOpacity);

    public static SolidColorBrush Tertiary => Primary(TertiaryOpacity);

    /// <summary>Claudy's <c>Theme.Font.mono</c>: ports, commands, file names, what a developer types.</summary>
    public static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");

    /// <summary>An accent as text, readable on light and dark glass alike: Claudy's <c>.ink(_:)</c>.</summary>
    public static Rgba Ink(Ink ink) => Theme.Color(ink, IsDark);

    /// <summary>Claudy's <c>sectionLabel</c>: section titles and column headers, in sentence case.</summary>
    public static TextBlock SectionLabel(string text) => Text(text, 11, FontWeights.Medium, SecondaryOpacity);

    /// <summary>Claudy's <c>meta</c>: counts, times and captions under a figure.</summary>
    public static TextBlock Meta(string text) => Text(text, 11, FontWeights.Normal, SecondaryOpacity);

    /// <summary>Text in the monospaced face.</summary>
    public static TextBlock MonoText(string text, double size, FontWeight weight, double opacity = 1, Rgba? colour = null)
    {
        var block = Text(text, size, weight, opacity, colour);
        block.FontFamily = Mono;
        return block;
    }

    /// <summary>
    /// One block of the card, Claudy's <c>tile</c>: glass washed with the colour of what it shows.
    /// Windows has no Liquid Glass; this is Claudy's own fallback before macOS 26, a quiet fill and a
    /// hairline under the wash. <see cref="RestyleTile"/> follows the theme.
    /// </summary>
    public static Border Tile(UIElement child, Rgba? tint = null, double strength = 0.16, double? padding = null)
    {
        var tile = new Border
        {
            Child = child,
            Padding = new Thickness(padding ?? Theme.Metric("tilePadding")),
            CornerRadius = new CornerRadius(Theme.Metric("tileCorner")),
            BorderThickness = new Thickness(1),
        };
        RestyleTile(tile, tint, strength);
        return tile;
    }

    public static void RestyleTile(Border tile, Rgba? tint, double strength = 0.16)
    {
        // A tint reads weaker on dark glass: half as much again there.
        var wash = tint is { } colour
            ? colour with { A = (byte)Math.Round(255 * Math.Clamp(strength * (IsDark ? 1.5 : 1), 0, 1)) }
            : new Rgba(0, 0, 0, (byte)(IsDark ? 31 : 0));
        tile.Background = new SolidColorBrush(Color(wash.Over(PrimaryInk with { A = 13 })));
        tile.BorderBrush = Primary(0.08);
    }

    /// <summary>Claudy's <c>CountBadge</c>: a count in a small capsule washed with its colour, its symbol inside.</summary>
    public static Border CountBadge(int value, Ink ink, bool showsHand = false)
    {
        var colour = Ink(ink);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (showsHand)
        {
            row.Children.Add(Hand(9, colour));
        }
        row.Children.Add(Text(value.ToString(System.Globalization.CultureInfo.CurrentCulture), 11, FontWeights.SemiBold, 1, colour));
        return Capsule(new Border
        {
            Child = row,
            Padding = new Thickness(6, 0, 6, 0),
            MinWidth = 18,
            Height = 18,
            Background = Brush(Theme.Color(ink, isDark: true), IsDark ? 0.22 : 0.16),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    /// <summary>
    /// Claudy's <c>PillButtonStyle</c>: a capsule button. Prominent, it is filled with its tint and
    /// lettered in dark ink; quiet, the tint only washes it.
    /// </summary>
    public static Button PillButton(string title, Action action, Rgba? tint = null, bool isProminent = true, bool isLarge = false, string? glyph = null)
    {
        var colour = tint ?? Theme.Color(Accent.Coral);
        var ink = isProminent ? Theme.OnAccent : colour;
        var label = Text(title, isLarge ? 14 : 12, FontWeights.SemiBold, 1, ink);
        UIElement content = glyph is null ? label : Row(5, Icon(glyph, isLarge ? 13 : 11, 1, ink), label);
        var height = isLarge ? 38 : 26;
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(isLarge ? 16 : 12, 0, isLarge ? 16 : 12, 0),
            MinHeight = height,
            Height = height,
            MinWidth = 0,
            Background = isProminent ? Brush(colour) : Brush(colour, 0.16),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(height / 2.0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Resources["ButtonBackgroundPointerOver"] = isProminent ? Brush(colour, 0.9) : Brush(colour, 0.22);
        button.Resources["ButtonBackgroundPressed"] = isProminent ? Brush(colour, 0.8) : Brush(colour, 0.28);
        button.Resources["ButtonBackgroundDisabled"] = Brush(colour, isProminent ? 0.45 : 0.07);
        button.Resources["ButtonForegroundPointerOver"] = Brush(ink);
        button.Resources["ButtonForegroundPressed"] = Brush(ink);
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" })
        {
            button.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        button.Click += (_, _) => action();
        Name(button, title);
        return button;
    }

    /// <summary>
    /// SF Symbols' <c>hand.raised.fill</c>, which Segoe has no glyph for: an open palm, four fingers
    /// up and the thumb aside, drawn in a 16-unit box and scaled to <paramref name="size"/>.
    /// </summary>
    public static FrameworkElement Hand(double size, Rgba colour)
    {
        var shape = new GeometryGroup { FillRule = FillRule.Nonzero };
        void Rounded(double x, double y, double width, double height, double angle = 0)
        {
            var rectangle = new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, y, width, height) };
            if (angle != 0)
            {
                rectangle.Transform = new RotateTransform { Angle = angle, CenterX = x + (width / 2), CenterY = y + height };
            }
            shape.Children.Add(rectangle);
            // Round ends: a disc at each end of the finger.
            var radius = width / 2;
            foreach (var centreY in new[] { y + radius, y + height - radius })
            {
                var disc = new EllipseGeometry { Center = new Windows.Foundation.Point(x + radius, centreY), RadiusX = radius, RadiusY = radius };
                if (angle != 0)
                {
                    disc.Transform = new RotateTransform { Angle = angle, CenterX = x + (width / 2), CenterY = y + height };
                }
                shape.Children.Add(disc);
            }
        }
        Rounded(3.6, 2.4, 2.2, 8);
        Rounded(6.1, 1.0, 2.2, 9);
        Rounded(8.6, 1.6, 2.2, 8.5);
        Rounded(11.0, 3.4, 2.0, 7);
        Rounded(1.6, 6.6, 2.2, 5.6, -28);
        shape.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(3.6, 7.5, 9.4, 4) });
        shape.Children.Add(new EllipseGeometry { Center = new Windows.Foundation.Point(8.3, 11.2), RadiusX = 4.7, RadiusY = 3.9 });
        var path = new Microsoft.UI.Xaml.Shapes.Path { Data = shape, Fill = Brush(colour), Width = 16, Height = 16, Stretch = Stretch.None };
        return new Viewbox { Child = new Grid { Width = 16, Height = 16, Children = { path } }, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Claudy's <c>RoundGlass</c>: a small round control, a quiet disc under its symbol.</summary>
    public static (Button Button, FontIcon Icon) RoundButton(string glyph, Action action, string tooltip, double size = 28)
    {
        var icon = Icon(glyph, 11, SecondaryOpacity);
        var disc = new Border
        {
            Child = icon,
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Primary(0.08),
        };
        return (Plain(disc, action, tooltip), icon);
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

    /// <summary>
    /// Claudy's <c>Hairline</c>: the thin line that separates two subjects lying on the card's glass,
    /// where a tile would have been one block too many. Inset by six points along its length.
    /// </summary>
    public static Border Rule(bool isVertical = false) => new()
    {
        Width = isVertical ? 1 : double.NaN,
        Height = isVertical ? double.NaN : 1,
        Background = Primary(0.1),
        Margin = isVertical ? new Thickness(0, 6, 0, 6) : new Thickness(6, 0, 6, 0),
    };

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
