using Claudio.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App;

/// <summary>
/// The right-click menu of the notification-area icon and of the island, as Claudy's
/// <c>ClaudyMenu</c>: refresh, the other placements, the account, quit. The icon draws it as a
/// Win32 menu, the island as a WinUI one; both read their entries here.
/// </summary>
internal sealed class ClaudyMenu(UsageViewModel model, Action quit)
{
    /// <summary>One line of the menu; a null title is a separator. No action shows it disabled.</summary>
    public sealed record Entry(string? Title, Action? Action = null)
    {
        public static Entry Separator { get; } = new(Title: null);
    }

    /// <summary>The menu for Claudio as it stands now.</summary>
    public IReadOnlyList<Entry> Entries()
    {
        var entries = new List<Entry> { new("Refresh", () => _ = model.RefreshAsync(userInitiated: true)) };
        foreach (var placement in model.Placement.Offered(model.HasNotchedScreen))
        {
            entries.Add(new(placement.MenuTitle(), () => model.Place(placement)));
        }
        if (AccountEntry() is { } account)
        {
            entries.Add(Entry.Separator);
            entries.Add(account);
        }
        entries.Add(Entry.Separator);
        entries.Add(new("Quit Claudio", quit));
        return entries;
    }

    /// <summary>The same entries as a WinUI menu.</summary>
    public MenuFlyout Flyout()
    {
        var menu = new MenuFlyout();
        foreach (var entry in Entries())
        {
            if (entry.Title is null)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                continue;
            }
            var item = new MenuFlyoutItem { Text = entry.Title, IsEnabled = entry.Action is not null };
            if (entry.Action is { } action)
            {
                item.Click += (_, _) => action();
            }
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>
    /// Sign in or out, whichever applies; nothing before the first reading, which has yet to say
    /// which. Claudy offers nothing on its demo set; Claudio's own sign-in reads the account without
    /// Claude Code, so the demo keeps the way in. Disabled while a sign-in is already under way.
    /// </summary>
    private Entry? AccountEntry()
    {
        if (model.IsSignedIn)
        {
            return new("Sign out of Claude", model.SignOut);
        }
        if (!model.HasLoaded)
        {
            return null;
        }
        return new("Sign in to Claude…", model.IsSigningIn ? null : model.StartSignIn);
    }
}
