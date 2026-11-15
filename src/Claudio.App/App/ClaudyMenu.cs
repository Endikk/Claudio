using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App;

/// <summary>
/// What the menus reach beyond the reading: Claudio shown or put away, the agent room, the export,
/// and whether the shortcuts reach Claudio at all. The app holds them; every menu reads them here.
/// </summary>
internal sealed record ClaudioActions(
    Action ToggleClaudy,
    Action ToggleRoom,
    Action ExportUsage,
    Action Quit,
    Func<bool> IsHotKeyActive,
    Func<bool> IsRoomHotKeyActive);

/// <summary>
/// The right-click menu of the notification-area icon and of the island, as Claudy's
/// <c>ClaudyMenu</c>: refresh, the other placements, the notifications, the shortcut, the agent
/// room, the export, the account, quit. The icon draws it as a Win32 menu, the island as a WinUI
/// one; both read their entries here.
/// </summary>
internal sealed class ClaudyMenu(UsageViewModel model, Notifier notifier, ClaudioActions actions)
{
    /// <summary>
    /// One line of the menu; a null title is a separator. No action shows it disabled; a header is
    /// a disabled title over its group; a check mark when <see cref="IsChecked"/> is set; the
    /// shortcut written beside it; a submenu instead of an action.
    /// </summary>
    public sealed record Entry(string? Title, Action? Action = null)
    {
        public static Entry Separator { get; } = new(Title: null);

        public bool? IsChecked { get; init; }
        public bool IsHeader { get; init; }
        public string? Shortcut { get; init; }
        public IReadOnlyList<Entry>? Submenu { get; init; }
    }

    /// <summary>The menu for Claudio as it stands now.</summary>
    public IReadOnlyList<Entry> Entries()
    {
        notifier.RefreshPermission();
        var entries = new List<Entry> { new("Refresh", () => _ = model.RefreshAsync(userInitiated: true)) };
        foreach (var placement in model.Placement.Offered(model.HasNotchedScreen))
        {
            entries.Add(new(placement.MenuTitle(), () => model.Place(placement)));
        }
        entries.Add(Entry.Separator);
        entries.Add(new("Notifications") { Submenu = NotificationEntries() });
        entries.AddRange(SharedEntries());
        if (AccountEntry() is { } account)
        {
            entries.Add(Entry.Separator);
            entries.Add(account);
        }
        entries.Add(Entry.Separator);
        entries.Add(new("Quit Claudio", actions.Quit));
        return entries;
    }

    /// <summary>The shortcut, the room and the export: the card's own menu carries them too.</summary>
    public IReadOnlyList<Entry> SharedEntries() =>
    [
        actions.IsHotKeyActive()
            ? new("Show or hide Claudio", actions.ToggleClaudy) { Shortcut = GlobalHotKey.Display }
            : new($"Show or hide Claudio ({GlobalHotKey.Display} is taken)", actions.ToggleClaudy),
        new("Open agent room", actions.ToggleRoom) { Shortcut = actions.IsRoomHotKeyActive() ? GlobalHotKey.RoomDisplay : null },
        new("Export usage as CSV…", model.IsExporting ? null : actions.ExportUsage),
    ];

    /// <summary>
    /// One switch per kind, in two groups. When notifications are turned off for Claudio in
    /// Windows' Settings the switches can do nothing: the first line says so and leads there.
    /// </summary>
    public IReadOnlyList<Entry> NotificationEntries()
    {
        var entries = new List<Entry>();
        if (notifier.IsDenied)
        {
            entries.Add(new("Turned off in Windows Settings…", Notifier.OpenSettings));
            entries.Add(Entry.Separator);
        }
        (string Title, IEnumerable<NotificationKind> Kinds)[] groups =
        [
            ("Quota", NotificationKinds.All.Where(kind => kind.IsQuota())),
            ("Sessions and agents", NotificationKinds.All.Where(kind => !kind.IsQuota())),
        ];
        for (var index = 0; index < groups.Length; index++)
        {
            if (index > 0)
            {
                entries.Add(Entry.Separator);
            }
            entries.Add(new(groups[index].Title) { IsHeader = true });
            foreach (var kind in groups[index].Kinds)
            {
                entries.Add(new(kind.MenuTitle(), () => notifier.Toggle(kind)) { IsChecked = notifier.IsEnabled(kind) });
            }
        }
        return entries;
    }

    /// <summary>The same entries as a WinUI menu.</summary>
    public MenuFlyout Flyout()
    {
        var menu = new MenuFlyout();
        foreach (var item in FlyoutItems(Entries()))
        {
            menu.Items.Add(item);
        }
        return menu;
    }

    public static IEnumerable<MenuFlyoutItemBase> FlyoutItems(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Title is null)
            {
                yield return new MenuFlyoutSeparator();
                continue;
            }
            if (entry.Submenu is { } submenu)
            {
                var sub = new MenuFlyoutSubItem { Text = entry.Title };
                foreach (var child in FlyoutItems(submenu))
                {
                    sub.Items.Add(child);
                }
                yield return sub;
                continue;
            }
            MenuFlyoutItem item = entry.IsChecked is { } isChecked
                ? new ToggleMenuFlyoutItem { Text = entry.Title, IsChecked = isChecked }
                : new MenuFlyoutItem { Text = entry.Title };
            item.IsEnabled = entry.Action is not null && !entry.IsHeader;
            if (entry.Shortcut is { } shortcut)
            {
                item.KeyboardAcceleratorTextOverride = shortcut;
            }
            if (entry.Action is { } action && !entry.IsHeader)
            {
                item.Click += (_, _) => action();
            }
            yield return item;
        }
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
