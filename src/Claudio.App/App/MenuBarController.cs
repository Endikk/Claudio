using System.Runtime.InteropServices;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// Claudio in the notification area, as Claudy in the menu bar: the mascot, typing while a session
/// runs, exploding once when a quota fills on screen and dead until it frees up, waving while a
/// new version is out, tinted by the gauge's band, with Claudy's coral dot when an update waits.
/// While Claudio lives there, the lead figure ("42%") sits in a second icon beside the mascot, as
/// Claudy writes it next to its own. Left click shows the card or the short card; right click opens
/// Claudy's short menu.
/// </summary>
internal sealed partial class MenuBarController : IDisposable
{
    private readonly TrayIconWithContextMenu _tray = new("Claudio.Tray");
    private readonly TrayIconWithContextMenu _figure = new("Claudio.Figure");
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _animation;
    private readonly Dictionary<string, nint> _icons = [];
    private readonly Mascot _mascot = Mascot.Shared;
    private readonly DesignTokens _tokens = DesignTokens.Shared;
    private readonly UsageViewModel _model;
    private readonly Action _quit;
    private (Rgba Tint, bool Dot, int Size) _style;
    private Pose _pose = Pose.Still;
    private DateTimeOffset _since = DateTimeOffset.UtcNow;
    private bool? _wasOverloaded;
    private string _menuKey = string.Empty;
    private string _figureKey = string.Empty;
    private nint _figureIcon;

    private enum Pose
    {
        Still,
        Typing,
        Exploding,
        Dead,
        Waving,
    }

    public MenuBarController(DispatcherQueue ui, Action click, UsageViewModel model, Action quit)
    {
        _ui = ui;
        _model = model;
        _quit = quit;
        _style = (_tokens.Color("color.accent.coral"), false, IconSize());
        _animation = ui.CreateTimer();
        _animation.IsRepeating = true;
        _animation.Tick += (_, _) => Advance();

        foreach (var icon in new[] { _tray, _figure })
        {
            // The tray lives on its own thread: every action is handed back to the UI thread.
            icon.MessageWindow.MouseEventReceived += (_, args) =>
            {
                if (args.MouseEvent == MouseEvent.IconLeftMouseUp)
                {
                    _ui.TryEnqueue(() => click());
                }
            };
            // Explorer restarted: its notification area starts empty, and the icons come back.
            icon.MessageWindow.TaskbarCreated += (_, _) => _ui.TryEnqueue(Recreate);
            // Another screen scale: the icons are redrawn at their new size.
            icon.MessageWindow.DpiChanged += (_, _) => _ui.TryEnqueue(Restyle);
        }
        _tray.ToolTip = "Claudio";
        _tray.Icon = Icon(_mascot.Poses["resting"], "resting");
        _tray.ContextMenu = Menu();
        _tray.Create();
        _figure.ToolTip = "Claudio";
        _figure.ContextMenu = Menu();
    }

    /// <summary>Follows a new reading: the tint of its band, the pose, the tooltips, the figure, the menu.</summary>
    public void Show(UsageSnapshot snapshot, bool isGreeting, bool hasUpdate, bool hasLoaded)
    {
        var now = DateTimeOffset.UtcNow;
        var lead = snapshot.Primary;
        var tint = Theme.Tint(lead.Accent, lead.Percent);
        var style = (tint, hasUpdate, IconSize());
        if (style != _style)
        {
            Forget();
            _style = style;
        }

        // Claudy's tooltip: the figure and when it resets, or just the name.
        var percent = lead.IsMeasured ? $"{UsageFormat.Percent(lead)}%" : UsageFormat.NoFigure;
        var tooltip = lead.IsActive(now) ? $"{lead.Title} {percent} · reset {UsageFormat.ResetTime(lead.ResetDate, now)}" : "Claudio";
        _tray.UpdateToolTip(tooltip);

        var reduceMotion = Motion.ReducesMotion;
        var overloaded = snapshot.IsOverloaded;
        // The placeholder before the first reading is not one: remembering it would turn the first
        // real reading of a full quota into an explosion at launch instead of the dead state.
        var isReading = hasLoaded && new[] { snapshot.Session, snapshot.Weekly, snapshot.Scoped, lead }.Any(window => window.IsMeasured);
        Pose pose;
        if (overloaded)
        {
            pose = _wasOverloaded == false && !reduceMotion ? Pose.Exploding
                : _pose == Pose.Exploding ? Pose.Exploding // a refresh mid-explosion must not cut it short
                : Pose.Dead;
        }
        else if (isGreeting && !reduceMotion)
        {
            // A full quota matters more than a new version: the dead state wins over the wave.
            pose = Pose.Waving;
        }
        else
        {
            pose = snapshot.Session.IsRunning(now) && !reduceMotion ? Pose.Typing : Pose.Still;
        }
        if (isReading)
        {
            _wasOverloaded = overloaded;
        }
        if (pose != _pose)
        {
            _pose = pose;
            _since = now;
            _animation.Stop();
            if (pose is not Pose.Still && !(pose == Pose.Dead && reduceMotion))
            {
                _animation.Interval = TimeSpan.FromMilliseconds(pose switch
                {
                    Pose.Typing => _mascot.TypingLoop[0].Milliseconds,
                    Pose.Exploding => 35,
                    Pose.Waving => 30,
                    _ => 130,
                });
                _animation.Start();
            }
        }
        Draw();
        ShowFigure(lead, tooltip);

        var menuKey = $"{_model.Placement}|{_model.IsSignedIn}|{_model.HasLoaded}|{_model.IsSigningIn}";
        if (menuKey != _menuKey)
        {
            _menuKey = menuKey;
            _tray.ContextMenu = Menu();
            _figure.ContextMenu = Menu();
        }
    }

    private void Advance()
    {
        if (_pose == Pose.Exploding && DateTimeOffset.UtcNow - _since >= _mascot.OverloadIntro)
        {
            _pose = Pose.Dead;
            _animation.Interval = TimeSpan.FromMilliseconds(130);
        }
        Draw();
    }

    private void Draw()
    {
        var elapsed = DateTimeOffset.UtcNow - _since;
        switch (_pose)
        {
            case Pose.Typing:
                var index = (int)(elapsed.TotalMilliseconds / _mascot.TypingLoop[0].Milliseconds) % _mascot.TypingLoop.Count;
                _tray.UpdateIcon(Icon(_mascot.TypingLoop[index], _mascot.TypingLoop[index].Name));
                break;
            case Pose.Exploding:
                var blast = _mascot.Overload[_mascot.OverloadFrameIndex(elapsed)];
                _tray.UpdateIcon(Icon(Crop(blast), blast.Name));
                break;
            case Pose.Dead:
                // Opening on a full quota shows the dead state directly, as the card does; a still
                // frame of it when Windows' animations are off.
                var dead = _mascot.Overload[_mascot.OverloadFrameIndex(Motion.ReducesMotion ? null : _mascot.OverloadIntro + elapsed)];
                _tray.UpdateIcon(Icon(Crop(dead), dead.Name));
                break;
            case Pose.Waving:
                // The wave is drawn on a taller grid than it needs: its empty rows go first.
                var wave = _mascot.Wave[_mascot.WaveFrameIndex(elapsed)];
                var (first, count) = _mascot.WaveContentRows;
                _tray.UpdateIcon(Icon(wave with { Rows = wave.Rows.Skip(first).Take(count).ToList() }, $"wave-{wave.Name}"));
                break;
            default:
                _tray.UpdateIcon(Icon(_mascot.Poses["resting"], "resting"));
                break;
        }
    }

    /// <summary>
    /// The figure beside the mascot, while Claudio lives in the notification area: in the taskbar's
    /// own ink, as Claudy's figure in the menu bar's.
    /// </summary>
    private void ShowFigure(UsageWindow lead, string tooltip)
    {
        var wanted = _model.Placement == Placement.NotificationArea && _model.IsSignedIn;
        if (!wanted)
        {
            if (_figure.IsCreated)
            {
                _figure.TryRemove();
            }
            return;
        }
        var text = TrayFigure.Text(lead.IsMeasured ? lead.Percent : null);
        var ink = TaskbarIsLight() ? new Rgba(0, 0, 0) : new Rgba(255, 255, 255);
        var size = IconSize();
        var key = $"{text}|{ink}|{size}";
        if (key != _figureKey)
        {
            _figureKey = key;
            var previous = _figureIcon;
            var bytes = TrayIconImage.ToIconResource(TrayFigure.Paint(text, ink, size));
            _figureIcon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconVersion, size, size, 0);
            if (_figure.IsCreated)
            {
                _figure.UpdateIcon(_figureIcon);
            }
            else
            {
                _figure.Icon = _figureIcon;
            }
            if (previous != 0)
            {
                DestroyIcon(previous);
            }
        }
        if (!_figure.IsCreated)
        {
            _figure.Icon = _figureIcon;
            _figure.Create();
        }
        _figure.UpdateToolTip(tooltip);
    }

    /// <summary>The explosion grid is wider than the sprite: the icon keeps the sprite's own square.</summary>
    private SpriteFrame Crop(SpriteFrame frame)
    {
        var sprite = _mascot.Poses["resting"];
        var (column, row) = _mascot.SpriteOrigin;
        var rows = frame.Rows.Skip(row).Take(sprite.Rows.Count)
                        .Select(line => line.Substring(column, Math.Min(sprite.Columns, line.Length - column)))
                        .ToList();
        return frame with { Rows = rows };
    }

    /// <summary>Claudy's short menu: refresh, the other placement, the account, quit.</summary>
    private PopupMenu Menu()
    {
        var menu = new PopupMenu();
        menu.Items.Add(new PopupMenuItem("Refresh", (_, _) => _ui.TryEnqueue(() => _ = _model.RefreshAsync(userInitiated: true))));
        foreach (var placement in _model.Placement.Offered())
        {
            menu.Items.Add(new PopupMenuItem(placement.MenuTitle(), (_, _) => _ui.TryEnqueue(() => _model.Place(placement))));
        }
        if (_model.IsSignedIn)
        {
            menu.Items.Add(new PopupMenuSeparator());
            menu.Items.Add(new PopupMenuItem("Sign out of Claude", (_, _) => _ui.TryEnqueue(_model.SignOut)));
        }
        else if (_model.HasLoaded)
        {
            menu.Items.Add(new PopupMenuSeparator());
            // Disabled while a sign-in is already under way.
            menu.Items.Add(new PopupMenuItem("Sign in to Claude…", (_, _) => _ui.TryEnqueue(_model.StartSignIn)) { Enabled = !_model.IsSigningIn });
        }
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(new PopupMenuItem("Quit Claudio", (_, _) => _ui.TryEnqueue(() => _quit())));
        return menu;
    }

    /// <summary>One icon per frame, tint and dot, made once: the loops only swap handles.</summary>
    private nint Icon(SpriteFrame frame, string key)
    {
        if (_icons.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var size = _style.Size;
        var crisp = TrayIconImage.ForIcon(frame, size);
        var image = TrayIconImage.Fit(TrayIconImage.Paint(crisp, _style.Tint, _mascot, _tokens), size);
        if (_style.Dot)
        {
            image = TrayIconImage.WithUpdateDot(image, _tokens.Color("color.accent.coral"));
        }
        var bytes = TrayIconImage.ToIconResource(image);
        var handle = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconVersion, size, size, 0);
        _icons[key] = handle;
        return handle;
    }

    private void Forget()
    {
        foreach (var handle in _icons.Values)
        {
            DestroyIcon(handle);
        }
        _icons.Clear();
    }

    private void Restyle()
    {
        Forget();
        _style = _style with { Size = IconSize() };
        _figureKey = string.Empty;
        Draw();
    }

    private void Recreate()
    {
        try
        {
            _tray.TryRemove();
            _tray.Create();
            Draw();
            if (_figure.IsCreated)
            {
                _figure.TryRemove();
                _figure.Create();
            }
        }
#pragma warning disable CA1031 // A notification area that is not back yet is tried again at the next restart.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"tray: could not come back after Explorer restarted: {error.Message}");
        }
    }

    private static int IconSize() => Math.Max(16, GetSystemMetrics(SmallIconWidth));

    /// <summary>The taskbar's own theme, which can differ from the apps': the figure takes its ink.</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light == 1;
    }

    public void Dispose()
    {
        _animation.Stop();
        _figure.Dispose();
        _tray.Dispose();
        Forget();
        if (_figureIcon != 0)
        {
            DestroyIcon(_figureIcon);
        }
    }

    private const int SmallIconWidth = 49; // SM_CXSMICON
    private const uint IconVersion = 0x00030000;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool isIcon,
                                                         uint version, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);
}
