using Claudio.App.Views;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Claudio.App;

/// <summary>
/// The floating card, Claudy's <c>RootView</c> in its window: loading, onboarding, minimal or full,
/// the account card over it, the red hairline past 95 %, and the right-click menu. It settles in
/// the bottom-right corner above the clock and can be dragged anywhere; it does not take a place
/// in the taskbar or in Alt+Tab.
/// </summary>
internal sealed partial class RootView : FloatingPanel
{
    private enum Display
    {
        Loading,
        Onboarding,
        Minimal,
        Full,
    }

    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly LoadingCard _loading = new();
    private readonly OnboardingView _onboarding;
    private readonly MinimalView _minimal;
    private readonly FullView _full;
    private readonly Action _quit;
    private Display? _shown;
    private bool _profileShown;
    private bool _isTyping;
    private string? _shape;

    public RootView(UsageViewModel model, UpdateChecker updates, PortsViewModel ports, Action quit)
    {
        _model = model;
        _updates = updates;
        _quit = quit;
        _onboarding = new OnboardingView(model);
        _minimal = new MinimalView(model, updates);
        _full = new FullView(model, updates, ports);
        Title = "Claudio";

        // Any click on the card counts as opening Claudio: the wave has been seen.
        Card.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => updates.AcknowledgeGreeting()), handledEventsToo: true);
        Card.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            var position = args.TryGetPosition(Card, out var point) ? point : new Point(Card.ActualWidth / 2, 24);
            Menu().ShowAt(Card, position);
        };
        ThemeFlipped += Update;

        // Claudy's ⌘R: Ctrl+R, or F5, refreshes whenever the card has the keyboard.
        foreach (var (key, modifiers) in new[] { (Windows.System.VirtualKey.R, Windows.System.VirtualKeyModifiers.Control), (Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None) })
        {
            Shortcut(key, modifiers, () => _ = _model.RefreshAsync(userInitiated: true));
        }
        // Claudy's Command-Q.
        Shortcut(Windows.System.VirtualKey.Q, Windows.System.VirtualKeyModifiers.Control, quit);
        // WinUI would otherwise announce the shortcut in a tooltip over the whole card.
        Card.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
    }

    private void Shortcut(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (sender, args) =>
        {
            args.Handled = true;
            action();
        };
        Card.KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>Shows the card for the first time, in its home corner.</summary>
    public void Present()
    {
        SetActivatable(false);
        Update();
        // Shown without taking the focus, as Claudy's non-activating panel: opening at sign-in must
        // not pull anyone out of what they are doing, a full-screen game included.
        AppWindow.Show(activateWindow: false);
        Anchor = HomeAnchor();
    }

    private Display Wanted =>
        !_model.HasLoaded ? Display.Loading
        : !_model.IsSignedIn ? Display.Onboarding
        : _model.IsMinimal ? Display.Minimal
        : Display.Full;

    public void Update()
    {
        var display = Wanted;
        if (display != _shown)
        {
            _shown = display;
            Face = display switch
            {
                Display.Loading => _loading,
                Display.Onboarding => _onboarding,
                Display.Minimal => _minimal,
                _ => _full,
            };
            SetCorner(display is Display.Minimal or Display.Loading ? Theme.Metric("minimalCorner") : Theme.Metric("cardCorner"));
        }
        switch (display)
        {
            case Display.Onboarding:
                _onboarding.Update();
                break;
            case Display.Minimal:
                _minimal.Update();
                break;
            case Display.Full:
                _full.Update();
                break;
        }
        // A new shape (mode, tab, details, update line) sends the card back to its corner, as
        // Claudy's does when its window resizes; a drag otherwise holds.
        var shape = $"{display}|{_model.Tab}|{_model.IsDetailsExpanded}|{_updates.Available is not null}";
        if (_shape is not null && shape != _shape)
        {
            ReturnHome();
        }
        _shape = shape;

        // Visible strain: past 95 %, and only in the modes that carry gauges.
        SetStrain(display is Display.Full or Display.Minimal ? _model.Snapshot.Strain(Theme.StrainThreshold) : 0);
        UpdateProfile(display);

        SetTopmost(_model.IsAlwaysOnTop);
        // The pasted sign-in code is the one thing typed into the card: only then does it take the keyboard.
        if (_model.IsAwaitingManualCode != _isTyping)
        {
            _isTyping = _model.IsAwaitingManualCode;
            SetActivatable(_isTyping);
            if (_isTyping)
            {
                Activate();
            }
        }
        FitToCard();
    }

    private void UpdateProfile(Display display)
    {
        var visible = _model.IsProfileVisible && display == Display.Full;
        if (visible == _profileShown)
        {
            return;
        }
        _profileShown = visible;
        if (!visible)
        {
            SetOverlay(null);
            return;
        }
        var dim = new Grid { Background = Ui.Brush(new Rgba(0, 0, 0), 0.22) };
        Ui.OnTap(dim, _model.ToggleProfile);
        var popup = new ProfilePopup(_model.Snapshot.Account, _model.IsSignedIn ? _model.SignOut : null, _model.ToggleProfile);
        // Claudy's `.shadow(color: .black.opacity(0.35), radius: 22, y: 10)` under the account card.
        var lifted = Views.OutlineShadow.Wrap(popup, 16, new Rgba(0, 0, 0, (byte)Math.Round(255 * 0.35)), 22, 10);
        lifted.HorizontalAlignment = HorizontalAlignment.Right;
        lifted.VerticalAlignment = VerticalAlignment.Top;
        lifted.Margin = new Thickness(0, 42, 12, 0);
        SetOverlay(new Grid { Children = { dim, lifted } });
    }

    /// <summary>The card comes and goes from the notification area icon, and keeps its place.</summary>
    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
        else
        {
            AppWindow.Show(activateWindow: false);
            _ = _model.RefreshAsync();
        }
    }

    public void ShowCard()
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show(activateWindow: false);
        }
        FitToCard();
    }

    public void HideCard() => AppWindow.Hide();

    /// <summary>Claudy's right-click menu, item for item.</summary>
    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Refresh", "\uE72C", () => _ = _model.RefreshAsync(userInitiated: true)));
        menu.Items.Add(Item(_model.IsMinimal ? "Full mode" : "Minimal mode", _model.IsMinimal ? "\uE740" : "\uE73F", _model.ToggleMode));
        foreach (var placement in _model.Placement.Offered())
        {
            menu.Items.Add(Item(placement.MenuTitle(), placement == Placement.NotificationArea ? "\uE7F4" : "\uE737", () => _model.Place(placement)));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        if (_model.IsSignedIn)
        {
            menu.Items.Add(Item("Sign out of Claude", null, _model.SignOut));
        }
        else if (_model.HasLoaded)
        {
            var signIn = Item("Sign in to Claude…", "\uE77B", _model.StartSignIn);
            signIn.IsEnabled = !_model.IsSigningIn;
            menu.Items.Add(signIn);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var onTop = new ToggleMenuFlyoutItem { Text = "Always on top", IsChecked = _model.IsAlwaysOnTop };
        onTop.Click += (_, _) => _model.SetAlwaysOnTop(onTop.IsChecked);
        menu.Items.Add(onTop);
        var atLogin = new ToggleMenuFlyoutItem { Text = "Launch at sign-in", IsChecked = _model.LaunchesAtLogin };
        atLogin.Click += (_, _) => _model.SetLaunchAtLogin(atLogin.IsChecked);
        menu.Items.Add(atLogin);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Quit Claudio", null, _quit));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string? glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph is not null)
        {
            item.Icon = new FontIcon { Glyph = glyph, FontFamily = Ui.Icons };
        }
        item.Click += (_, _) => action();
        return item;
    }
}
