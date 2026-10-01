using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// The card shown while no Claude session is open. Claudio never shows estimated quotas, so
/// signing in is the way in: this card replaces the gauges outright, no invented figures behind a veil.
/// </summary>
internal sealed partial class OnboardingView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly SignInControls _signIn;
    private readonly TextBlock _missing = Ui.Text("Claude Code was not found on this PC: sign in to Claude to read your quotas.", 10, Ui.Medium, 0.48);

    public OnboardingView(UsageViewModel model)
    {
        _model = model;
        _signIn = new SignInControls(model);
        Width = Theme.Metric("fullWidth");
        Padding = new Thickness(Theme.Metric("padding") + 6, 0, Theme.Metric("padding") + 6, 0);
        Children.Add(Mark());

        var name = Ui.Text("Claudio", 20, Ui.SemiBold, 0.92);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(name);
        var tagline = Ui.Text("Your real Claude quotas.", 11, Ui.Medium, 0.5);
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.Margin = new Thickness(0, 3, 0, 0);
        Children.Add(tagline);

        var features = Ui.Column(13,
            Feature("\uEC4A", Accent.Coral, "Real quotas", "The same figures as claude.ai, to the minute."),
            Feature("\uE916", Accent.Amber, "Your pace", "Ahead of or behind each window, at a glance."),
            Feature("\uEA18", Accent.Sage, "Nothing leaves your PC", "The only exchange is Anthropic's API, with your own token."));
        features.Margin = new Thickness(0, 20, 0, 20);
        Children.Add(features);

        _missing.TextWrapping = TextWrapping.Wrap;
        _missing.TextTrimming = TextTrimming.None;
        _missing.TextAlignment = TextAlignment.Center;
        _missing.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_missing);
        _signIn.Margin = new Thickness(0, 0, 0, 22);
        Children.Add(_signIn);
    }

    public void Update()
    {
        _missing.Visibility = _model.IsClaudeInstalled ? Visibility.Collapsed : Visibility.Visible;
        _signIn.Update();
    }

    /// <summary>The brand mark: the mascot typing in a soft coral glow.</summary>
    private static Grid Mark()
    {
        var coral = Theme.Color(Accent.Coral);
        var glow = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Windows.Foundation.Point(37, 37),
            GradientOrigin = new Windows.Foundation.Point(37, 37),
            RadiusX = 40,
            RadiusY = 40,
        };
        glow.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0.22), Offset = 0.1 });
        glow.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0), Offset = 1 });
        var mark = new Grid { Width = 74, Height = 74, Margin = new Thickness(0, 28, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        mark.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = glow });
        mark.Children.Add(new MascotView { Width = 58, Height = 54, IsTyping = true });
        return mark;
    }

    private static Grid Feature(string glyph, Accent accent, string title, string detail)
    {
        var tint = Theme.Color(accent);
        var icon = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            Background = Ui.Brush(tint, 0.14),
            Child = Ui.Icon(glyph, 12, 1, tint),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var text = Ui.Column(1.5, Ui.Text(title, 11.5, Ui.SemiBold, 0.88), Wrapped(Ui.Text(detail, 10, Ui.Regular, 0.48)));
        var row = new Grid { ColumnSpacing = 11 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    private static TextBlock Wrapped(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }
}

/// <summary>
/// The micro-card of the very first reading, before Claudio knows whether a session exists: it
/// spares a signed-in user a flash of onboarding.
/// </summary>
internal sealed partial class LoadingCard : Grid
{
    public LoadingCard()
    {
        Width = Theme.Metric("minimalWidth");
        Padding = new Thickness(0, 13, 0, 13);
        // SwiftUI centres the row in the strip's width.
        var row = Ui.Row(10, new MascotView { Width = 29, Height = 27, IsTyping = true },
                         new ProgressRing { Width = 16, Height = 16, IsActive = true, VerticalAlignment = VerticalAlignment.Center });
        row.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(row);
    }
}
