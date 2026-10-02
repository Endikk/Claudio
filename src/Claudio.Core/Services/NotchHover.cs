namespace Claudio.Core.Services;

/// <summary>
/// When the island opens and closes as the pointer comes and goes, as Claudy's <c>NotchHover</c>.
/// Opening waits a beat, so a pointer crossing the ears on its way elsewhere leaves the island
/// shut; closing waits a little longer, so grazing the edge does not snap it closed. The delays run
/// on <c>after</c>, the window's own timer in the app and a hand-driven clock in the tests.
/// </summary>
public sealed class NotchHover
{
    public static readonly TimeSpan DefaultOpenDelay = TimeSpan.FromSeconds(0.15);
    public static readonly TimeSpan DefaultCloseDelay = TimeSpan.FromSeconds(0.3);

    private readonly TimeSpan _openDelay;
    private readonly TimeSpan _closeDelay;
    private readonly Action _open;
    private readonly Action _close;
    private readonly Func<TimeSpan, Action, IDisposable> _after;
    private readonly Func<bool> _mayClose;
    private IDisposable? _pending;

    /// <param name="after">Runs an action once after a delay; disposing what it returns cancels it.</param>
    /// <param name="mayClose">False while the island must stay open whatever the pointer does: a text field in it is being edited.</param>
    public NotchHover(Action open, Action close, Func<TimeSpan, Action, IDisposable> after, Func<bool>? mayClose = null,
                      TimeSpan? openDelay = null, TimeSpan? closeDelay = null)
    {
        _open = open;
        _close = close;
        _after = after;
        _mayClose = mayClose ?? (() => true);
        _openDelay = openDelay ?? DefaultOpenDelay;
        _closeDelay = closeDelay ?? DefaultCloseDelay;
    }

    public bool IsOpen { get; private set; }

    /// <summary>Where the pointer was last reported: the window only speaks on crossings.</summary>
    public bool IsPointerInside { get; private set; }

    /// <summary>The pointer entered or left the island. Each call replaces whatever was pending.</summary>
    public void Crossed(bool inside)
    {
        IsPointerInside = inside;
        Cancel();
        if (inside)
        {
            if (IsOpen)
            {
                return;
            }
            _pending = _after(_openDelay, () =>
            {
                IsOpen = true;
                _open();
            });
        }
        else
        {
            if (!IsOpen)
            {
                return;
            }
            _pending = _after(_closeDelay, () =>
            {
                if (!_mayClose())
                {
                    return;
                }
                IsOpen = false;
                _close();
            });
        }
    }

    /// <summary>
    /// The window moved or resized under a pointer that may not have: no crossing was reported. Acts
    /// only when the pointer is not where it was last reported, so a timer already running the right
    /// way is left alone.
    /// </summary>
    public void Resync(bool pointerInside)
    {
        if (pointerInside != IsPointerInside)
        {
            Crossed(pointerInside);
        }
    }

    /// <summary>
    /// Closed at once with nothing pending, without calling <c>close</c>: the island is being hidden
    /// and its owner resets what it shows.
    /// </summary>
    public void Reset()
    {
        Cancel();
        IsOpen = false;
        IsPointerInside = false;
    }

    /// <summary>
    /// Opened or closed at once, the pointer being elsewhere: Windows' notification-area icon does
    /// it on a click. Opened that way, the island closes on the pointer's next exit.
    /// </summary>
    public void Toggle()
    {
        Cancel();
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _open();
        }
        else
        {
            _close();
        }
    }

    private void Cancel()
    {
        _pending?.Dispose();
        _pending = null;
    }
}
