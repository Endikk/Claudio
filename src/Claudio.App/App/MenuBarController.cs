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
    private readonly SideIcon _waiting = new("Claudio.Waiting");
    private readonly SideIcon _agents = new("Claudio.Agents");
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _animation;
    private readonly DispatcherQueueTimer _retry;
    private readonly Dictionary<string, nint> _icons = [];
    private readonly Mascot _mascot = Mascot.Shared;
    private readonly DesignTokens _tokens = DesignTokens.Shared;
    private readonly UsageViewModel _model;
    private readonly ClaudyMenu _menu;
    private (Rgba Tint, bool Dot, int Size) _style;
    private Pose _pose = Pose.Still;
    private DateTimeOffset _since = DateTimeOffset.UtcNow;
    private bool? _wasOverloaded;
    private string _menuKey = string.Empty;
    private string _figureKey = string.Empty;
    private nint _figureIcon;
    private nint _shown;

    private enum Pose
    {
        Still,
        Typing,
        Exploding,
        Dead,
        Waving,
    }

    public MenuBarController(DispatcherQueue ui, Action click, UsageViewModel model, ClaudyMenu menu)
    {
        _ui = ui;
        _model = model;
        _menu = menu;
        _style = (_tokens.Color("color.accent.coral"), false, IconSize());
        _animation = ui.CreateTimer();
        _animation.IsRepeating = true;
        _animation.Tick += (_, _) => Advance();

        foreach (var icon in new[] { _tray, _figure, _waiting.Icon, _agents.Icon })
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
        _figure.ToolTip = "Claudio";
        _figure.ContextMenu = Menu();
        _waiting.Icon.ContextMenu = Menu();
        _agents.Icon.ContextMenu = Menu();

        // The notification area may not take icons yet (Claudio launched at sign-in, before Explorer
        // drew it) or at all (a server, a session without a shell). Creating an icon then throws on the
        // library's own thread, which no handler can catch and which ends the process: so the icon
        // waits for the taskbar, and Claudio shows its card or island meanwhile.
        _retry = ui.CreateTimer();
        _retry.Interval = TimeSpan.FromSeconds(2);
        _retry.IsRepeating = true;
        _retry.Tick += (_, _) => CreateWhenReady();
        CreateWhenReady();
    }

    /// <summary>Puts the icon next to the clock as soon as there is a taskbar to put it on.</summary>
    private void CreateWhenReady()
    {
        if (_tray.IsCreated)
        {
            _retry.Stop();
            return;
        }
        if (!TaskbarExists())
        {
            if (!_retry.IsRunning)
            {
                DiagnosticLog.Append("tray: the notification area takes no icon yet, the icon waits for it");
                _retry.Start();
            }
            return;
        }
        _retry.Stop();
        _tray.Create();
        Draw();
    }

    private static bool TaskbarExists() => NotificationAreaProbe.Accepts();

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
        // While agents run, how many; while a session waits, that it does.
        if (_running > 0)
        {
            tooltip += " · " + (_running == 1 ? "1 agent running" : $"{_running} agents running");
        }
        if (_waitingCount > 0)
        {
            tooltip += " · " + (_waitingCount == 1 ? "a session waits for you" : $"{_waitingCount} sessions wait for you");
        }
        if (_tray.IsCreated)
        {
            _tray.UpdateToolTip(tooltip);
        }

        var reduceMotion = Motion.ReducesMotion;
        var overloaded = snapshot.IsOverloaded;
        // The placeholder before the first reading is not one: remembering it would turn the first
        // real reading of a full quota into an explosion at launch instead of the dead state.
        var isReading = hasLoaded && (snapshot.IsDemo || new[] { snapshot.Session, snapshot.Weekly, snapshot.Scoped, lead }.Any(window => window.IsMeasured));
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
                    Pose.Typing => TypingInterval,
                    Pose.Exploding => 35,
                    Pose.Waving => (int)Math.Ceiling(NextWaveFrame(TimeSpan.Zero).TotalMilliseconds),
                    _ => 130,
                });
                _animation.Start();
            }
        }
        Draw();
        ShowFigure(lead, tooltip);
        ShowTeam(tooltip);

        var menuKey = Describe(_menu.Entries());
        if (menuKey != _menuKey)
        {
            _menuKey = menuKey;
            _tray.ContextMenu = Menu();
            _figure.ContextMenu = Menu();
            _waiting.Icon.ContextMenu = Menu();
            _agents.Icon.ContextMenu = Menu();
        }
    }

    private int _running;
    private int _waitingCount;
    private Rgba? _leadTint;

    /// <summary>
    /// The agents at work and the sessions waiting, as Claudy reads them for its menu bar title:
    /// finished agents are not counted, and the tint is the one of the agent at work.
    /// </summary>
    public void ShowTeam(int running, Rgba? leadTint, int waiting)
    {
        _running = running;
        _leadTint = leadTint;
        _waitingCount = waiting;
    }

    /// <summary>
    /// After the figure, as Claudy's title runs on: an amber raised hand while a session waits, then
    /// the lead agent's Claudy and the count while agents run. Only while Claudio lives in the
    /// notification area, as the figure.
    /// </summary>
    private void ShowTeam(string tooltip)
    {
        var here = _model.Placement.Effective(_model.HasNotchedScreen) == Placement.NotificationArea && TaskbarExists();
        var size = IconSize();
        var amber = _tokens.Color("color.accent.amber");
        _waiting.Show(here && _waitingCount > 0, $"hand|{size}", () => TrayAgents.PaintHand(amber, size), tooltip, size);
        _agents.Show(here && _running > 0 && _leadTint is not null, $"agents|{_running}|{_leadTint}|{size}",
                     () => TrayAgents.PaintAgents(_running, _leadTint ?? amber, size, _mascot, _tokens), tooltip, size);
    }

    /// <summary>One more square next to the clock, drawn from a picture and swapped only when it changes.</summary>
    private sealed class SideIcon(string name) : IDisposable
    {
        private string _key = string.Empty;
        private nint _handle;

        public TrayIconWithContextMenu Icon { get; } = new(name);

        public void Show(bool wanted, string key, Func<Rgba[,]> paint, string tooltip, int size)
        {
            if (!wanted)
            {
                if (Icon.IsCreated)
                {
                    Icon.TryRemove();
                }
                return;
            }
            if (key != _key)
            {
                _key = key;
                var previous = _handle;
                var bytes = TrayIconImage.ToIconResource(paint());
                _handle = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconVersion, size, size, 0);
                if (Icon.IsCreated)
                {
                    Icon.UpdateIcon(_handle);
                }
                else
                {
                    Icon.Icon = _handle;
                }
                if (previous != 0)
                {
                    DestroyIcon(previous);
                }
            }
            if (!Icon.IsCreated)
            {
                Icon.Icon = _handle;
                Icon.ToolTip = tooltip;
                Icon.Create();
            }
            Icon.UpdateToolTip(tooltip);
        }

        /// <summary>Explorer restarted, or the scale changed: drawn and created again at the next reading.</summary>
        public void Reset()
        {
            if (Icon.IsCreated)
            {
                Icon.TryRemove();
            }
            _key = string.Empty;
        }

        public void Dispose()
        {
            Icon.Dispose();
            if (_handle != 0)
            {
                DestroyIcon(_handle);
            }
        }
    }

    /// <summary>
    /// How long a frame of the typing stays next to the clock: twice the card's, as Claudy's in the
    /// menu bar, since each new image has the shell redraw the whole icon.
    /// </summary>
    private int TypingInterval => _mascot.TypingLoop[0].Milliseconds * 2;

    /// <summary>Past the change by a hair, so that the time read back lands on the new frame.</summary>
    private TimeSpan NextWaveFrame(TimeSpan elapsed) => _mascot.TimeToNextWaveFrame(elapsed) + TimeSpan.FromMilliseconds(2);

    private void Advance()
    {
        if (_pose == Pose.Exploding && DateTimeOffset.UtcNow - _since >= _mascot.OverloadIntro)
        {
            _pose = Pose.Dead;
            _animation.Interval = TimeSpan.FromMilliseconds(130);
        }
        if (_pose == Pose.Waving)
        {
            // Woken when the frame changes, not thirty times a second in between.
            _animation.Interval = NextWaveFrame(DateTimeOffset.UtcNow - _since);
        }
        Draw();
    }

    private void Draw()
    {
        if (!_tray.IsCreated)
        {
            return;
        }
        var elapsed = DateTimeOffset.UtcNow - _since;
        switch (_pose)
        {
            case Pose.Typing:
                var index = (int)(elapsed.TotalMilliseconds / TypingInterval) % _mascot.TypingLoop.Count;
                Put(Icon(_mascot.TypingLoop[index], _mascot.TypingLoop[index].Name));
                break;
            case Pose.Exploding:
                var blast = _mascot.Overload[_mascot.OverloadFrameIndex(elapsed)];
                Put(Icon(Crop(blast), blast.Name));
                break;
            case Pose.Dead:
                // Opening on a full quota shows the dead state directly, as the card does; a still
                // frame of it when Windows' animations are off.
                var dead = _mascot.Overload[_mascot.OverloadFrameIndex(Motion.ReducesMotion ? null : _mascot.OverloadIntro + elapsed)];
                Put(Icon(Crop(dead), dead.Name));
                break;
            case Pose.Waving:
                // The wave is drawn on a taller grid than it needs: its empty rows go first.
                var wave = _mascot.Wave[_mascot.WaveFrameIndex(elapsed)];
                var (first, count) = _mascot.WaveContentRows;
                Put(Icon(wave with { Rows = wave.Rows.Skip(first).Take(count).ToList() }, $"wave-{wave.Name}"));
                break;
            default:
                Put(Icon(_mascot.Poses["resting"], "resting"));
                break;
        }
    }

    /// <summary>
    /// Puts a frame next to the clock, unless it is the one already there: each change of icon has
    /// the shell redraw it, which costs far more than the frame itself.
    /// </summary>
    private void Put(nint icon)
    {
        if (icon == _shown)
        {
            return;
        }
        _shown = icon;
        _tray.UpdateIcon(icon);
    }

    /// <summary>
    /// The figure beside the mascot, while Claudio lives in the notification area: in the taskbar's
    /// own ink, as Claudy's figure in the menu bar's.
    /// </summary>
    private void ShowFigure(UsageWindow lead, string tooltip)
    {
        var wanted = _model.Placement.Effective(_model.HasNotchedScreen) == Placement.NotificationArea && (_model.IsSignedIn || _model.Snapshot.IsDemo);
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
        if (!_figure.IsCreated && TaskbarExists())
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

    /// <summary>Claudy's short menu, <see cref="ClaudyMenu"/>, as a Win32 menu.</summary>
    private PopupMenu Menu()
    {
        var menu = new PopupMenu();
        foreach (var item in MenuItems(_menu.Entries()))
        {
            menu.Items.Add(item);
        }
        return menu;
    }

    private IEnumerable<PopupItem> MenuItems(IEnumerable<ClaudyMenu.Entry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Title is null)
            {
                yield return new PopupMenuSeparator();
                continue;
            }
            if (entry.Submenu is { } submenu)
            {
                var sub = new PopupSubMenu(entry.Title);
                foreach (var child in MenuItems(submenu))
                {
                    sub.Items.Add(child);
                }
                yield return sub;
                continue;
            }
            // The tray lives on its own thread: every action is handed back to the UI thread. A
            // shortcut goes after a tab, which Windows aligns on the right as menus write it.
            var action = entry.IsHeader ? null : entry.Action;
            var text = entry.Shortcut is { } shortcut ? $"{entry.Title}	{shortcut}" : entry.Title;
            yield return new PopupMenuItem(text, (_, _) => _ui.TryEnqueue(() => action?.Invoke()))
            {
                Enabled = action is not null,
                Checked = entry.IsChecked == true,
            };
        }
    }

    /// <summary>What the menu says now, to rebuild it only when that changes.</summary>
    private static string Describe(IEnumerable<ClaudyMenu.Entry> entries) =>
        string.Join('|', entries.Select(entry => $"{entry.Title}{entry.IsChecked}{entry.Action is null}{entry.Shortcut}[{(entry.Submenu is null ? string.Empty : Describe(entry.Submenu))}]"));

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
        _shown = 0;
    }

    private void Restyle()
    {
        Forget();
        _waiting.Reset();
        _agents.Reset();
        _style = _style with { Size = IconSize() };
        _figureKey = string.Empty;
        Draw();
    }

    private void Recreate()
    {
        try
        {
            _tray.TryRemove();
            _figure.TryRemove();
            _waiting.Reset();
            _agents.Reset();
            _shown = 0;
            // Explorer says its taskbar is back, but the window it is found by may take a moment.
            CreateWhenReady();
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
        _retry.Stop();
        _figure.Dispose();
        _waiting.Dispose();
        _agents.Dispose();
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
