using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// The way in, wherever Claudio shows it signed out: the onboarding card and the notification-area
/// flyout. It covers the whole sign-in, including the field for pasting the code when the loopback
/// port is taken, so neither face can get stuck halfway.
/// </summary>
internal sealed partial class SignInControls : ContentControl
{
    private readonly UsageViewModel _model;
    private readonly TextBox _code = new() { PlaceholderText = "code#state", FontSize = 11, IsTextScaleFactorEnabled = false };
    private string? _shown;

    public SignInControls(UsageViewModel model)
    {
        _model = model;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Rebuilt only when the step changes: a code half typed must survive a refresh.</summary>
    public void Update()
    {
        var step = $"{_model.IsAwaitingManualCode}|{_model.IsSigningIn}|{_model.ErrorMessage}";
        if (step == _shown)
        {
            return;
        }
        _shown = step;
        if (_model.IsAwaitingManualCode)
        {
            Content = ManualEntry();
        }
        else if (_model.IsSigningIn)
        {
            Content = Waiting();
        }
        else
        {
            Content = SignInButton();
        }
    }

    private StackPanel ManualEntry()
    {
        var coral = Theme.Color(Accent.Coral);
        var submit = new Button
        {
            Content = Ui.Text("Submit", 11, Ui.SemiBold, 1, Ui.White),
            Background = Ui.Brush(coral),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 5),
            MinHeight = 0,
        };
        submit.Resources["ButtonBackgroundPointerOver"] = Ui.Brush(coral, 0.88);
        submit.Resources["ButtonBackgroundPressed"] = Ui.Brush(coral, 0.75);
        submit.Resources["ButtonBackgroundDisabled"] = Ui.Brush(coral, 0.4);
        submit.Click += (_, _) => _model.SubmitManualCode(_code.Text);
        submit.IsEnabled = false;
        _code.Text = string.Empty;
        _code.FontFamily = Ui.Font;
        _code.TextChanged += (_, _) => submit.IsEnabled = _code.Text.Trim().Length > 0;
        _code.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && _code.Text.Trim().Length > 0)
            {
                _model.SubmitManualCode(_code.Text);
            }
        };
        return Ui.Column(7,
            Ui.Text("Paste the code the page shows:", 10.5, Ui.Medium, 0.6),
            _code,
            Ui.Row(12, submit, Cancel()));
    }

    private Grid Waiting()
    {
        var ring = new ProgressRing { Width = 14, Height = 14, IsActive = true };
        var waiting = Ui.Spread(Ui.Row(9, ring, Ui.Text("Waiting for the browser…", 10.5, Ui.Medium, 0.6)), Cancel());
        waiting.Padding = new Thickness(0, 6, 0, 6);
        return waiting;
    }

    private Button Cancel() => Ui.Plain(Ui.Text("Cancel", 10.5, Ui.Medium, 0.5), _model.CancelSignIn);

    /// <summary>The coral capsule, its warm glow under it, and the reassurance below.</summary>
    private StackPanel SignInButton()
    {
        var coral = Theme.Color(Accent.Coral);
        var fill = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(coral), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0.78), Offset = 1 });
        var label = Ui.Text("Sign in to Claude", 12.5, Ui.SemiBold, 1, Ui.White);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var capsule = Ui.Capsule(new Border
        {
            Child = label,
            Background = fill,
            Padding = new Thickness(0, 10, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        capsule.PointerEntered += (_, _) => capsule.Opacity = 0.92;
        capsule.PointerExited += (_, _) => capsule.Opacity = 1;
        Ui.OnTap(capsule, _model.StartSignIn, "Sign in to Claude");
        var button = OutlineShadow.Wrap(capsule, -1, coral with { A = (byte)Math.Round(255 * 0.38) }, 9, 2);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;

        var note = Ui.Text("Official claude.ai sign-in, revocable at any time.", 9, Ui.Medium, 0.35);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        var column = Ui.Column(9, button, note);
        if (_model.ErrorMessage is { } message)
        {
            var error = Ui.Text(message, 9.5, Ui.Medium, 0.9, Theme.Danger);
            error.TextWrapping = TextWrapping.Wrap;
            error.TextTrimming = TextTrimming.None;
            column.Children.Add(error);
        }
        return column;
    }
}
