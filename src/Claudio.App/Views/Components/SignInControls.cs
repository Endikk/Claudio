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
        var submit = Ui.PillButton("Submit", () => _model.SubmitManualCode(_code.Text));
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
            Ui.Text("Paste the code the page shows:", 11, Ui.Medium, Ui.SecondaryOpacity),
            _code,
            Ui.Row(12, submit, Cancel()));
    }

    private Grid Waiting()
    {
        var ring = new ProgressRing { Width = 14, Height = 14, IsActive = true };
        var waiting = Ui.Spread(Ui.Row(9, ring, Ui.Text("Waiting for the browser…", 11, Ui.Medium, Ui.SecondaryOpacity)), Cancel());
        waiting.Padding = new Thickness(0, 6, 0, 6);
        return waiting;
    }

    private Button Cancel() => Ui.Plain(Ui.Text("Cancel", 11, Ui.Medium, Ui.SecondaryOpacity), _model.CancelSignIn);

    /// <summary>The coral capsule, and the reassurance below.</summary>
    private StackPanel SignInButton()
    {
        var button = Ui.PillButton("Sign in to Claude", _model.StartSignIn, isLarge: true);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;

        var note = Ui.Text("Official claude.ai sign-in, revocable at any time", 11, Ui.Medium, Ui.SecondaryOpacity);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        var column = Ui.Column(9, button, note);
        if (_model.ErrorMessage is { } message)
        {
            var error = Ui.Text(message, 11, Ui.Medium, 0.9, Theme.Danger);
            error.TextWrapping = TextWrapping.Wrap;
            error.TextTrimming = TextTrimming.None;
            column.Children.Add(error);
        }
        return column;
    }
}
