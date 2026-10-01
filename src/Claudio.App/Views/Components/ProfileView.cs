using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>The account chip at the top right of the header.</summary>
internal sealed partial class AvatarButton : Grid
{
    private readonly Border _disc = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
    private readonly TextBlock _initial = Ui.Text("?", 11, Ui.Bold, 1, new Rgba(255, 255, 255));

    public AvatarButton(Action action)
    {
        _initial.HorizontalAlignment = HorizontalAlignment.Center;
        _initial.VerticalAlignment = VerticalAlignment.Center;
        _disc.Child = _initial;
        _disc.Background = Gradient(0.65);
        // Claudy's `.shadow(color: coral.opacity(0.4), radius: 5, y: 1)`.
        var coral = Theme.Color(Accent.Coral);
        Children.Add(OutlineShadow.Wrap(_disc, 12, coral with { A = (byte)Math.Round(255 * 0.4) }, 5, 1));
        Ui.Tooltip(this, "Account");
        Ui.OnTap(this, action, "Account");
    }

    public void Update(Account account, bool isActive)
    {
        _initial.Text = account.Initial;
        _disc.BorderBrush = Ui.Brush(Ui.White, isActive ? 0.85 : 0.22);
    }

    public static LinearGradientBrush Gradient(double endOpacity)
    {
        var coral = Theme.Color(Accent.Coral);
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        gradient.GradientStops.Add(new GradientStop { Color = Ui.Color(coral), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, endOpacity), Offset = 1 });
        return gradient;
    }
}

/// <summary>
/// The account card, drawn inside the card rather than as a separate popup: who is signed in, the
/// plan and the organisation, and the way out.
/// </summary>
internal sealed partial class ProfilePopup : Grid
{
    public ProfilePopup(Account account, Action? onSignOut, Action onClose)
    {
        Width = 262;
        Padding = new Thickness(14);
        CornerRadius = new CornerRadius(16);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Brush(new Rgba(255, 255, 255), 0.14);
        Background = Ui.IsDark ? Ui.Brush(new Rgba(0x2B, 0x29, 0x28), 0.98) : Ui.Brush(new Rgba(0xF7, 0xF5, 0xF3), 0.98);

        var initial = Ui.Text(account.Initial, 15, Ui.Bold, 1, new Rgba(255, 255, 255));
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        var avatar = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = AvatarButton.Gradient(0.6), Child = initial };

        var identity = Ui.Column(2, Ui.Text(account.Name, 13, Ui.SemiBold, 1));
        if (account.Email.Length > 0)
        {
            identity.Children.Add(Ui.Text(account.Email, 10.5, Ui.Regular, 0.5));
        }
        var close = Ui.Plain(Ui.Icon("\uE711", 9, 0.4), onClose);
        close.Width = 18;
        close.Height = 18;
        close.VerticalAlignment = VerticalAlignment.Top;

        var top = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(identity, 1);
        Grid.SetColumn(close, 2);
        top.Children.Add(avatar);
        top.Children.Add(identity);
        top.Children.Add(close);

        var content = Ui.Column(0, top);
        (string Text, Accent Tint)[] badges =
        [
            (account.Plan, Accent.Coral),
            (account.Organization, Accent.Sky),
            (account.IsAdmin ? "Admin" : string.Empty, Accent.Sage),
        ];
        var shown = badges.Where(badge => badge.Text.Length > 0).ToList();
        if (shown.Count == 0)
        {
            content.Children.Add(Ui.Text("No Claude account signed in on this PC", 10, Ui.Regular, 0.4));
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (text, tint) in shown)
            {
                row.Children.Add(Ui.Pill(text, Theme.Color(tint), stroked: false));
            }
            content.Children.Add(row);
        }

        if (onSignOut is not null)
        {
            content.Children.Add(new Border { Height = 1, Background = Ui.Primary(0.1), Margin = new Thickness(0, 11, 0, 11) });
            content.Children.Add(PopupRow("Sign out", "\uF3B1", Theme.Danger, onSignOut));
        }
        Children.Add(content);
    }

    /// <summary>An action row, highlighted on hover.</summary>
    private static Border PopupRow(string title, string glyph, Rgba tint, Action action)
    {
        var icon = Ui.Icon(glyph, 10, 0.85, tint);
        icon.Width = 14;
        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Background = Ui.Primary(0),
            Child = Ui.Row(8, icon, Ui.Text(title, 11.5, Ui.Medium, 0.85, tint)),
        };
        row.PointerEntered += (_, _) => row.Background = Ui.Primary(0.08);
        row.PointerExited += (_, _) => row.Background = Ui.Primary(0);
        Ui.OnTap(row, action);
        return row;
    }
}
