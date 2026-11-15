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
    private readonly TextBlock _missing = Ui.Text("Claude Code was not found on this PC: sign in to Claude to read your quotas.", 11, Ui.Regular, Ui.SecondaryOpacity);

    public OnboardingView(UsageViewModel model)
    {
        _model = model;
        _signIn = new SignInControls(model);
        Width = Theme.Metric("fullWidth");
        Padding = new Thickness(Theme.Metric("panelPadding"), 0, Theme.Metric("panelPadding"), 0);
        Children.Add(Mark());

        var name = Ui.Text("Claudio", 22, Ui.SemiBold);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(name);
        var tagline = Ui.Text("Your real Claude quotas", 13, Ui.Regular, Ui.SecondaryOpacity);
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(tagline);

        var features = Ui.Column(14,
            Feature("\uEC4A", Accent.Coral, "Real quotas", "The same figures as claude.ai, to the minute"),
            Feature("\uE916", Accent.Sky, "Your pace", "Ahead of or behind each window, at a glance"),
            Feature("\uEA18", Accent.Teal, "Nothing leaves your PC", "The only exchange is Anthropic's API, with your own token"));
        features.Margin = new Thickness(12, 22, 12, 22);
        Children.Add(features);

        _missing.TextWrapping = TextWrapping.Wrap;
        _missing.TextTrimming = TextTrimming.None;
        _missing.TextAlignment = TextAlignment.Center;
        _missing.Margin = new Thickness(6, 0, 6, 12);
        Children.Add(_missing);
        _signIn.Margin = new Thickness(6, 0, 6, 22);
        Children.Add(_signIn);
    }

    public void Update()
    {
        _missing.Visibility = _model.IsClaudeInstalled ? Visibility.Collapsed : Visibility.Visible;
        _signIn.Update();
    }

    /// <summary>The brand mark: the mascot typing.</summary>
    private static MascotView Mark() => new()
    {
        Width = 64,
        Height = 60,
        IsTyping = true,
        Margin = new Thickness(0, 28, 0, 14),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Grid Feature(string glyph, Accent accent, string title, string detail)
    {
        var icon = Ui.Icon(glyph, 15, 1, Theme.Color(accent));
        icon.Width = 22;
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 0, 0);
        var text = Ui.Column(2, Ui.Text(title, 13, Ui.SemiBold), Wrapped(Ui.Text(detail, 12, Ui.Regular, Ui.SecondaryOpacity)));
        var row = new Grid { ColumnSpacing = 12 };
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
