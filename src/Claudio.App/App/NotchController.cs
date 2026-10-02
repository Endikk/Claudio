using Claudio.App.Views;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// Claudio at the top of the screen, as Claudy's <c>NotchController</c> round the notch: the ears at
/// rest, the island open underneath on hover. The app shows it instead of the card when the user
/// picks it; the icon next to the clock stays, and a click on it opens or closes the island.
/// </summary>
internal sealed class NotchController : IDisposable
{
    /// <summary>Long enough for the closing spring to come to rest.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(0.5);

    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly DispatcherQueue _ui;
    private readonly ClaudyMenu _menu;
    private readonly NotchHover _hover;
    private readonly DispatcherQueueTimer _watch;
    private readonly DispatcherQueueTimer _settle;
    private NotchPanel? _panel;
    private NotchView? _view;
    private NotchLayout? _layout;
    private double _scale = 1;
    private ScreenRect _frame;
    private ScreenRect? _settleFrame;
    private bool _isMenuOpen;
    private bool _isTyping;
    private bool _isWanted;
    private bool _stepsAside;
    private int _ticks;

    public NotchController(UsageViewModel model, UpdateChecker updates, DispatcherQueue ui, Action quit)
    {
        _model = model;
        _updates = updates;
        _ui = ui;
        _menu = new ClaudyMenu(model, quit);
        _hover = new NotchHover(Open, Close, After, () => !_model.IsAwaitingManualCode && !_isMenuOpen);
        // Made at launch with Claudio's other windows: made later, once another window had been
        // shown, the island's window showed on screen without ever drawing its content.
        MakePanel();

        // Four times a second while shown: the pointer's real place, which a window that moves or
        // resizes under a still pointer never hears about, and a full-screen app coming to the front.
        _watch = ui.CreateTimer();
        _watch.Interval = TimeSpan.FromMilliseconds(250);
        _watch.IsRepeating = true;
        _watch.Tick += (_, _) => Watch();

        _settle = ui.CreateTimer();
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            if (_settleFrame is { } frame)
            {
                _settleFrame = null;
                SetFrame(frame);
                Resync();
            }
        };
    }

    /// <summary>The island opened: what it shows makes the update bubble redundant.</summary>
    public event Action? Opened;

    /// <summary>The island at rest on screen, in pixels; null while it is not shown.</summary>
    public RectInt32? RestingBounds
    {
        get
        {
            if (_panel?.IsShown != true || _layout is null)
            {
                return null;
            }
            return Pixels(_layout.RestingFrame);
        }
    }

    /// <summary>
    /// Puts the island on its notch. A new notch rectangle (another resolution, another screen)
    /// closes it first, so the window never spans the old place and the new one.
    /// </summary>
    public void Show(NotchGeometry geometry, double scale)
    {
        _isWanted = true;
        var panel = _panel ?? MakePanel();
        if (_layout?.Geometry != geometry || Math.Abs(_scale - scale) > 0.001)
        {
            _layout = new NotchLayout(geometry);
            _scale = scale;
            Collapse();
            _view!.Notch = new Size(geometry.Notch.Width, geometry.Notch.Height);
            _frame = default;
            SetFrame(_layout.RestingFrame);
        }
        _stepsAside = PanelChrome.IsFullScreenAppInFront();
        if (!_stepsAside)
        {
            panel.ShowPanel();
        }
        _watch.Start();
        Update();
        Resync();
    }

    /// <summary>Takes the island away, closed, with nothing left pending.</summary>
    public void Hide()
    {
        _isWanted = false;
        _watch.Stop();
        Collapse();
        if (_layout is not null && _panel is not null)
        {
            SetFrame(_layout.RestingFrame);
        }
        _panel?.HidePanel();
    }

    /// <summary>
    /// The screens just changed: the notch may have moved. Hidden at once rather than left where its
    /// old frame now falls, until the delayed pass shows it again where it belongs.
    /// </summary>
    public void HideIfMoved(NotchGeometry? current)
    {
        if (_panel?.IsShown == true && current != _layout?.Geometry)
        {
            Hide();
        }
    }

    /// <summary>A click on the icon next to the clock: open, or closed again.</summary>
    public void Toggle()
    {
        if (_panel?.IsShown == true)
        {
            _hover.Toggle();
        }
    }

    public void Update()
    {
        if (_view is null || _panel is null)
        {
            return;
        }
        _view.Update();
        // The manual sign-in code is typed in the open island: only then does it take the keyboard.
        if (_model.IsAwaitingManualCode != _isTyping)
        {
            _isTyping = _model.IsAwaitingManualCode;
            _panel.SetTyping(_isTyping && _view.IsOpen);
        }
    }

    public void Dispose()
    {
        _watch.Stop();
        _settle.Stop();
        _panel?.Close();
    }

    private NotchPanel MakePanel()
    {
        _view = new NotchView(_model, _updates);
        _view.ShapeChanged += Fit;
        var panel = new NotchPanel(_view);
        panel.PointerCrossed += _hover.Crossed;
        panel.MenuRequested += ShowMenu;
        _panel = panel;
        return panel;
    }

    private void Collapse()
    {
        _hover.Reset();
        _settle.Stop();
        _settleFrame = null;
        _view?.Close(animated: false);
    }

    private void Open()
    {
        // Opening the island counts as opening Claudio: the wave has been seen.
        _updates.AcknowledgeGreeting();
        WidenForOpening();
        _view?.Open(animated: !Motion.ReducesMotion);
        if (_isTyping)
        {
            _panel?.SetTyping(true);
        }
        Opened?.Invoke();
    }

    private void Close() => _view?.Close(animated: !Motion.ReducesMotion);

    /// <summary>
    /// The island grows sideways as well as down. Widening the window first, while the island is
    /// still at rest, keeps that out of the animation: widened mid-flight, the island would slide.
    /// </summary>
    private void WidenForOpening()
    {
        if (_layout is null || _view is null)
        {
            return;
        }
        _settle.Stop();
        _settleFrame = null;
        var wide = _layout.Frame(new ScreenSize(_view.OpenWidth, _frame.Height));
        var (now, _) = NotchLayout.Step(_frame, wide);
        if (now != _frame)
        {
            SetFrame(now);
        }
    }

    /// <summary>Fits the window to the shape: at once when it grows, after the animation when it shrinks.</summary>
    private void Fit(ScreenSize shape)
    {
        if (_layout is null || shape.Width <= 0 || shape.Height <= 0)
        {
            return;
        }
        var (now, settle) = NotchLayout.Step(_frame, _layout.Frame(shape));
        if (now != _frame)
        {
            SetFrame(now);
        }
        _settle.Stop();
        _settleFrame = null;
        if (settle == now)
        {
            return;
        }
        // Without the spring there is no closing to wait for.
        _settleFrame = settle;
        _settle.Interval = Motion.ReducesMotion ? TimeSpan.FromMilliseconds(1) : SettleDelay;
        _settle.Start();
    }

    private void SetFrame(ScreenRect frame)
    {
        _frame = frame;
        _panel?.SetFrame(Pixels(frame));
    }

    private RectInt32 Pixels(ScreenRect frame)
    {
        int Px(double value) => (int)Math.Round(value * _scale);
        var left = Px(frame.X);
        var top = Px(frame.Y);
        return new RectInt32(left, top, Px(frame.Right) - left, Px(frame.Bottom) - top);
    }

    private void Watch()
    {
        if (_panel is null || !_isWanted)
        {
            return;
        }
        // Every two seconds: a game or a presentation running full screen in front.
        if (++_ticks % 8 == 0)
        {
            var stepsAside = PanelChrome.IsFullScreenAppInFront();
            if (stepsAside != _stepsAside)
            {
                _stepsAside = stepsAside;
                if (stepsAside)
                {
                    Collapse();
                    if (_layout is not null)
                    {
                        SetFrame(_layout.RestingFrame);
                    }
                    _panel.HidePanel();
                }
                else
                {
                    _panel.ShowPanel();
                }
            }
        }
        Resync();
    }

    /// <summary>
    /// A window that shrinks away from a still pointer, or appears under one, hears no crossing:
    /// the pointer's real place decides.
    /// </summary>
    private void Resync()
    {
        if (_panel?.IsShown == true && !_isMenuOpen)
        {
            _hover.Resync(_panel.HoldsCursor());
        }
    }

    private void ShowMenu(Microsoft.UI.Xaml.FrameworkElement target, Point position)
    {
        var menu = _menu.Flyout();
        // The ears are a small window: the menu opens outside it.
        menu.ShouldConstrainToRootBounds = false;
        _isMenuOpen = true;
        // Claudy's menu holds everything still while it is up: an island about to open stays shut,
        // or it would open under the menu and take it away.
        if (!_hover.IsOpen)
        {
            _hover.Reset();
        }
        menu.Closed += (_, _) =>
        {
            _isMenuOpen = false;
            // The pointer went to the menu and may have come back or not, unreported either way.
            _hover.Crossed(_panel?.HoldsCursor() == true);
        };
        menu.ShowAt(target, new FlyoutShowOptions { Position = position });
    }

    /// <summary>The hover's delays, on the window's own timer; disposing one cancels it.</summary>
    private Pending After(TimeSpan delay, Action action)
    {
        var timer = _ui.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (sender, _) =>
        {
            sender.Stop();
            action();
        };
        timer.Start();
        return new Pending(timer);
    }

    private sealed class Pending(DispatcherQueueTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
