using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The island opens on a pointer that stays, not on one passing through, and closes a moment after
/// the pointer leaves, as in Claudy's NotchHoverTests. The delays run on a clock the tests move.
/// </summary>
public sealed class NotchHoverTests
{
    private readonly Clock _clock = new();
    private int _opens;
    private int _closes;
    private bool _mayClose = true;

    private NotchHover MakeHover() => new(() => _opens++, () => _closes++, _clock.After, () => _mayClose);

    /// <summary>Several times either delay.</summary>
    private void Settle() => _clock.Advance(TimeSpan.FromSeconds(1));

    [Fact]
    public void APointerCrossingOnItsWayElsewhereLeavesItShut()
    {
        var hover = MakeHover();

        hover.Crossed(inside: true);
        hover.Crossed(inside: false);
        Settle();

        Assert.Equal(0, _opens);
        Assert.False(hover.IsOpen);
    }

    [Fact]
    public void APointerThatStaysOpensIt()
    {
        var hover = MakeHover();

        hover.Crossed(inside: true);
        _clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(0, _opens);
        Settle();

        Assert.Equal(1, _opens);
        Assert.True(hover.IsOpen);
    }

    [Fact]
    public void LeavingClosesItAfterTheDelay()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        hover.Crossed(inside: false);
        Assert.Equal(0, _closes);
        Settle();

        Assert.Equal(1, _closes);
        Assert.False(hover.IsOpen);
    }

    [Fact]
    public void ComingBackBeforeTheDelayKeepsItOpen()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        hover.Crossed(inside: false);
        hover.Crossed(inside: true);
        Settle();

        Assert.Equal(0, _closes);
        Assert.True(hover.IsOpen);
        Assert.Equal(1, _opens);
    }

    [Fact]
    public void AnEditInProgressKeepsItOpenUntilTheNextExit()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        _mayClose = false;
        hover.Crossed(inside: false);
        Settle();
        Assert.Equal(0, _closes);
        Assert.True(hover.IsOpen);

        _mayClose = true;
        hover.Crossed(inside: false);
        Settle();
        Assert.Equal(1, _closes);
    }

    [Fact]
    public void ResetCancelsAPendingOpen()
    {
        var hover = MakeHover();

        hover.Crossed(inside: true);
        hover.Reset();
        Settle();

        Assert.Equal(0, _opens);
        Assert.False(hover.IsOpen);
    }

    [Fact]
    public void ResetClosesWithoutCallingClose()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        hover.Reset();
        Settle();

        Assert.False(hover.IsOpen);
        Assert.Equal(0, _closes);
    }

    /// <summary>The window shrank away from a pointer that did not move: no exit was reported, the resync closes it.</summary>
    [Fact]
    public void AWindowShrinkingAwayFromAStillPointerClosesIt()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        hover.Resync(pointerInside: false);
        Settle();

        Assert.Equal(1, _closes);
        Assert.False(hover.IsOpen);
    }

    [Fact]
    public void AnIslandAppearingUnderThePointerOpens()
    {
        var hover = MakeHover();

        hover.Resync(pointerInside: true);
        Settle();

        Assert.Equal(1, _opens);
        Assert.True(hover.IsOpen);
    }

    [Fact]
    public void AResyncThatAgreesChangesNothing()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();

        hover.Resync(pointerInside: true);
        Settle();

        Assert.Equal(1, _opens);
        Assert.Equal(0, _closes);
        Assert.True(hover.IsOpen);
    }

    [Fact]
    public void AfterAResetThePointerCountsAsOutside()
    {
        var hover = MakeHover();
        hover.Crossed(inside: true);
        Settle();
        hover.Reset();

        hover.Resync(pointerInside: true);
        Settle();

        Assert.Equal(2, _opens);
    }

    /// <summary>Windows' icon next to the clock opens the island at once, the pointer being there.</summary>
    [Fact]
    public void TheIconOpensAndClosesItAtOnce()
    {
        var hover = MakeHover();

        hover.Toggle();
        Assert.True(hover.IsOpen);
        Assert.Equal(1, _opens);

        hover.Toggle();
        Assert.False(hover.IsOpen);
        Assert.Equal(1, _closes);
    }

    /// <summary>Opened from the icon, it stays until the pointer has been in and out.</summary>
    [Fact]
    public void OpenedFromTheIconItClosesOnThePointersNextExit()
    {
        var hover = MakeHover();
        hover.Toggle();

        hover.Resync(pointerInside: false);
        Settle();
        Assert.True(hover.IsOpen);

        hover.Crossed(inside: true);
        hover.Crossed(inside: false);
        Settle();
        Assert.False(hover.IsOpen);
        Assert.Equal(1, _closes);
    }

    /// <summary>A hand-driven clock: actions run when it is moved past their time, unless cancelled.</summary>
    private sealed class Clock
    {
        private readonly List<(TimeSpan At, Action Action, Handle Handle)> _queue = [];
        private TimeSpan _now;

        public IDisposable After(TimeSpan delay, Action action)
        {
            var handle = new Handle();
            _queue.Add((_now + delay, action, handle));
            return handle;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var (at, action, handle) in _queue.Where(item => item.At <= _now).OrderBy(item => item.At).ToList())
            {
                _queue.RemoveAll(item => item.Handle == handle);
                if (!handle.IsCancelled)
                {
                    action();
                }
            }
        }

        private sealed class Handle : IDisposable
        {
            public bool IsCancelled { get; private set; }

            public void Dispose() => IsCancelled = true;
        }
    }
}
