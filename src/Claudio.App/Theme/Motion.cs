using System.Diagnostics;
using Claudio.Core.Design;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>Windows' own say on motion.</summary>
internal static class Motion
{
    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    /// <summary>
    /// "Animation effects" is off in Windows' accessibility settings: the mascot holds still, as
    /// Claudy's does under macOS's "Reduce motion".
    /// </summary>
    public static bool ReducesMotion => !Settings.AnimationsEnabled;
}

/// <summary>
/// A value that moves to its target on one of Claudy's springs (response, damping fraction), one
/// step per rendered frame: the gauges, the rings and the bars all settle the way SwiftUI's do.
/// </summary>
internal sealed class SpringValue
{
    private readonly Action<double> _apply;
    private readonly double _stiffness;
    private readonly double _damping;
    private readonly Stopwatch _clock = new();
    private double _velocity;
    private double _target;
    private bool _isRunning;

    public SpringValue(Spring spring, Action<double> apply, double initial = 0)
    {
        _apply = apply;
        var omega = 2 * Math.PI / spring.ResponseSeconds;
        _stiffness = omega * omega;
        _damping = 2 * spring.DampingFraction * omega;
        Value = initial;
        _target = initial;
    }

    public static Spring Gauge { get; } = DesignTokens.Shared.Spring("gauge");

    public double Value { get; private set; }

    public void Set(double target, bool animated = true)
    {
        _target = target;
        if (!animated)
        {
            Stop();
            Value = target;
            _velocity = 0;
            _apply(target);
            return;
        }
        if (!_isRunning && Math.Abs(target - Value) > 1e-4)
        {
            _isRunning = true;
            _clock.Restart();
            CompositionTarget.Rendering += Step;
        }
    }

    private void Step(object? sender, object e)
    {
        var dt = Math.Min(_clock.Elapsed.TotalSeconds, 1.0 / 30);
        _clock.Restart();
        // Semi-implicit Euler, split into small steps so a stiff spring stays stable.
        const int Substeps = 4;
        for (var step = 0; step < Substeps; step++)
        {
            var h = dt / Substeps;
            var acceleration = (-_stiffness * (Value - _target)) - (_damping * _velocity);
            _velocity += acceleration * h;
            Value += _velocity * h;
        }
        if (Math.Abs(Value - _target) < 1e-4 && Math.Abs(_velocity) < 1e-3)
        {
            Value = _target;
            Stop();
        }
        _apply(Value);
    }

    private void Stop()
    {
        if (_isRunning)
        {
            CompositionTarget.Rendering -= Step;
            _isRunning = false;
        }
    }
}
