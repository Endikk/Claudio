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
    private readonly Border _disc = new() { Width = 26, Height = 26, CornerRadius = new CornerRadius(13) };
    private readonly TextBlock _initial = Ui.Text("?", 12, Ui.SemiBold, 1, Theme.OnAccent);

    public AvatarButton(Action action)
    {
        _initial.HorizontalAlignment = HorizontalAlignment.Center;
        _initial.VerticalAlignment = VerticalAlignment.Center;
        _disc.Child = _initial;
        _disc.Background = Ui.Brush(Theme.Color(Accent.Coral));
        Children.Add(_disc);
        Ui.Tooltip(this, "Account");
        Ui.OnTap(this, action, "Account");
    }

    public void Update(Account account, bool isActive)
    {
        _initial.Text = account.Initial;
        _disc.BorderBrush = Ui.Brush(Ui.White, isActive ? 0.85 : 0.3);
        _disc.BorderThickness = new Thickness(isActive ? 1.5 : 0.5);
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
        Padding = new Thickness(16);
        CornerRadius = new CornerRadius(20);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Brush(new Rgba(255, 255, 255), 0.14);
        Background = Ui.IsDark ? Ui.Brush(new Rgba(0x2B, 0x29, 0x28), 0.98) : Ui.Brush(new Rgba(0xF7, 0xF5, 0xF3), 0.98);

        var initial = Ui.Text(account.Initial, 15, Ui.SemiBold, 1, Theme.OnAccent);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        var avatar = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Ui.Brush(Theme.Color(Accent.Coral)), Child = initial };

        var identity = Ui.Column(2, Ui.Text(account.Name, 13, Ui.SemiBold, 1));
        if (account.Email.Length > 0)
        {
            var email = Ui.Text(account.Email, 11, Ui.Regular, Ui.SecondaryOpacity);
            email.TextTrimming = TextTrimming.Clip;
            identity.Children.Add(email);
        }
        var close = Ui.Plain(Ui.Icon("\uE711", 9, Ui.SecondaryOpacity), onClose);
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
            content.Children.Add(Ui.Meta("No Claude account signed in on this PC"));
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (text, tint) in shown)
            {
                var label = Ui.Text(text, 12, Ui.Medium, Ui.SecondaryOpacity);
                row.Children.Add(Ui.Row(4, Ui.Dot(6, Theme.Color(tint)), label));
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
            Child = Ui.Row(8, icon, Ui.Text(title, 12, Ui.Medium, 0.85, tint)),
        };
        row.PointerEntered += (_, _) => row.Background = Ui.Primary(0.08);
        row.PointerExited += (_, _) => row.Background = Ui.Primary(0);
        Ui.OnTap(row, action);
        return row;
    }
}
