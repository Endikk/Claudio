using System.Runtime.InteropServices.WindowsRuntime;
using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// A borderless, transparent window holding one of Claudy's glass cards: the rounded glass, its
/// gradient sheen and coral halo, the hairline, and the drop shadow drawn in a transparent margin
/// around it (<see cref="CardShadow.Inset"/>). The window follows the card's size and keeps its
/// bottom-right corner where it was, so the card grows up and to the left, never under the taskbar.
/// </summary>
internal partial class FloatingPanel : Window
{
    private readonly double _inset = CardShadow.Inset;
    private readonly double _margin = Theme.Metric("screenMargin");
    private readonly Grid _root = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Image _shadow = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Border _glass = new();
    private readonly Border _sheen = new();
    private readonly Border _glow = new();
    private readonly Border _hairline = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly Border _strain = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly ContentPresenter _face = new();
    private readonly Grid _overlay = new() { Visibility = Visibility.Collapsed };
    private double _corner = Theme.Metric("cardCorner");
    private (Size Card, double Scale, double Corner) _shadowKey;
    private PointInt32? _anchor;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _displayWatch;
    private bool _isAdjusting;

    // Dragging from anywhere on the card, past a few pixels; below that a press stays a click.
    private const double DragThreshold = 3;
    private PointInt32? _pressCursor;
    private PointInt32 _pressWindow;
    private bool _isDragging;

    public FloatingPanel()
    {
        Card = new Grid
        {
            Margin = new Thickness(_inset),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _glass, _sheen, _glow, _face, _hairline, _strain, _overlay },
        };
        _root.Children.Add(_shadow);
        _root.Children.Add(Card);
        Content = _root;
        SystemBackdrop = new TransparentBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Claudio.ico"));
        PanelChrome.RemoveFrame(AppWindow);

        _root.ActualThemeChanged += (_, _) => ThemeChanged();
        // A screen unplugged or a taskbar moved: once the system has settled, the card is put
        // back on a screen that exists, clamped into its work area.
        TransparentBackdrop.DisplayChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            _displayWatch ??= DispatcherQueue.CreateTimer();
            _displayWatch.Stop();
            _displayWatch.Interval = TimeSpan.FromMilliseconds(500);
            _displayWatch.IsRepeating = false;
            _displayWatch.Tick -= Resettle;
            _displayWatch.Tick += Resettle;
            _displayWatch.Start();
        });
        _root.Loaded += (_, _) =>
        {
            ThemeChanged();
            // Moved to a screen of another scale: the shadow is redrawn in its pixels.
            var scale = Scale;
            Content.XamlRoot.Changed += (_, _) =>
            {
                if (Math.Abs(Scale - scale) > 0.001)
                {
                    scale = Scale;
                    Ui.Scale = scale;
                    FitToCard();
                    ThemeFlipped?.Invoke();
                }
            };
        };
        Card.SizeChanged += (_, _) => FitToCard();
        Card.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Pressed), handledEventsToo: true);
        Card.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Moved), handledEventsToo: true);
        Card.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Released), handledEventsToo: true);
        // The card losing its own capture ends a drag; a child losing it, when the card takes it, does not.
        Card.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            if (_isDragging && ReferenceEquals(args.OriginalSource, Card))
            {
                EndDrag(args.Pointer);
            }
        }), handledEventsToo: true);
    }

    /// <summary>The card itself, without the shadow margin.</summary>
    protected Grid Card { get; }

    /// <summary>Raised whenever the window settles somewhere new: a resize, a drag, a screen change.</summary>
    public event Action? Placed;

    /// <summary>The visible card on screen, in pixels, without the shadow margin; null while hidden.</summary>
    public RectInt32? VisualBounds
    {
        get
        {
            if (!AppWindow.IsVisible)
            {
                return null;
            }
            var inset = (int)Math.Round(_inset * Scale);
            var (position, size) = (AppWindow.Position, AppWindow.Size);
            return new RectInt32(position.X + inset, position.Y + inset, size.Width - (2 * inset), size.Height - (2 * inset));
        }
    }

    /// <summary>The visible card's width in pixels, without the shadow margin; 0 until laid out.</summary>
    protected int VisualWidth
    {
        get
        {
            if (Card.ActualWidth <= 0)
            {
                return 0;
            }
            Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (int)Math.Ceiling((Card.DesiredSize.Width - (2 * _inset)) * Scale);
        }
    }

    /// <summary>The visible card's height in pixels, without the shadow margin; 0 until laid out.</summary>
    protected int VisualHeight
    {
        get
        {
            if (Card.ActualHeight <= 0)
            {
                return 0;
            }
            Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (int)Math.Ceiling((Card.DesiredSize.Height - (2 * _inset)) * Scale);
        }
    }

    /// <summary>Raised when the system theme flips, so the views redraw their inks.</summary>
    public event Action? ThemeFlipped;

    /// <summary>The bottom-right corner of the visible card, in screen pixels; null until placed.</summary>
    protected PointInt32? Anchor
    {
        get => _anchor;
        set
        {
            _anchor = value;
            FitToCard();
        }
    }

    protected bool IsDraggable { get; set; } = true;

    /// <summary>
    /// Sends the card back to its corner of the screen it is on, wherever it had been dragged.
    /// Claudy does it when the card changes shape (mode, details, onboarding); a few pixels of
    /// measuring noise are no reason, or a card just dropped would jump back.
    /// </summary>
    protected void ReturnHome()
    {
        if (_anchor is not null)
        {
            _anchor = HomeAnchor();
        }
    }

    protected UIElement? Face
    {
        get => _face.Content as UIElement;
        set => _face.Content = value;
    }

    protected double Scale => Content.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>The card's corner: 15 for the minimal strip and the loading card, 20 otherwise.</summary>
    protected void SetCorner(double corner)
    {
        if (Math.Abs(_corner - corner) > 0.01)
        {
            _corner = corner;
            Restyle();
            FitToCard();
        }
    }

    /// <summary>Past 95 % the card's hairline turns red, at an intensity rising to 100 %. The only overload signal: discreet, kept to the edge.</summary>
    protected void SetStrain(double strain)
    {
        _strain.BorderBrush = Ui.Brush(Theme.Danger, 0.28 * strain);
        _strain.Visibility = strain > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Something drawn over the card, clipped to it: the account card and its dimmed backdrop.</summary>
    protected void SetOverlay(UIElement? overlay)
    {
        _overlay.Children.Clear();
        if (overlay is not null)
        {
            _overlay.Children.Add(overlay);
        }
        _overlay.Visibility = overlay is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Resettle(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (AppWindow.IsVisible)
        {
            FitToCard();
        }
    }

    /// <summary>The bottom-right corner of the work area of the screen the card is on, inside the margin.</summary>
    protected PointInt32 HomeAnchor()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)Math.Round(_margin * Scale);
        return new PointInt32(area.X + area.Width - margin, area.Y + area.Height - margin);
    }

    /// <summary>Puts the window around the card, on its anchor, then clamps the card into the work area.</summary>
    protected void FitToCard()
    {
        if (_anchor is not { } anchor || Card.ActualWidth <= 0 || Card.ActualHeight <= 0 || _isDragging)
        {
            return;
        }
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var scale = Scale;
        var cardSize = new Size(Card.DesiredSize.Width - (2 * _inset), Card.DesiredSize.Height - (2 * _inset));
        var inset = (int)Math.Round(_inset * scale);
        var width = (int)Math.Ceiling(Card.DesiredSize.Width * scale);
        var height = (int)Math.Ceiling(Card.DesiredSize.Height * scale);

        var area = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
        var margin = (int)Math.Round(_margin * scale);
        var visualWidth = width - (2 * inset);
        var visualHeight = height - (2 * inset);
        var right = Math.Clamp(anchor.X, area.X + margin + visualWidth, area.X + area.Width - margin);
        var bottom = Math.Clamp(anchor.Y, Math.Min(area.Y + margin + visualHeight, area.Y + area.Height - margin), area.Y + area.Height - margin);
        _anchor = new PointInt32(right, bottom);

        _isAdjusting = true;
        var frame = new RectInt32(right + inset - width, bottom + inset - height, width, height);
        AppWindow.MoveAndResize(frame);
        _isAdjusting = false;
        DrawShadow(cardSize);
        Clip(cardSize);
        Reach(frame, area, inset, scale);
        Placed?.Invoke();
    }

    /// <summary>
    /// Where the window takes clicks: the card and the part of its shadow that shows, never the
    /// taskbar. The transparent margin would otherwise catch the clicks meant for the icons next to
    /// the clock, or for the window behind the card; the shadow fades out within these few pixels.
    /// </summary>
    private void Reach(RectInt32 frame, RectInt32 area, int inset, double scale)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var left = Math.Max(inset - Px(10), area.X - frame.X);
        var top = Math.Max(inset - Px(6), area.Y - frame.Y);
        var right = Math.Min(frame.Width - inset + Px(10), area.X + area.Width - frame.X);
        var bottom = Math.Min(frame.Height - inset + Px(16), area.Y + area.Height - frame.Y);
        PanelChrome.SetReach(AppWindow, left, top, right, bottom);
    }

    /// <summary>Above every other window, or among them.</summary>
    protected void SetTopmost(bool topmost) => PanelChrome.SetTopmost(AppWindow, topmost);

    /// <summary>
    /// A window that never takes the focus, as Claudy's non-activating panel: a click on the card
    /// leaves the keyboard where it was. Typing (the sign-in code) needs it back.
    /// </summary>
    protected void SetActivatable(bool activatable) => PanelChrome.SetActivatable(AppWindow, activatable);

    private void DrawShadow(Size card)
    {
        if (_shadowKey == (card, Scale, _corner))
        {
            return;
        }
        _shadowKey = (card, Scale, _corner);
        var (pixels, width, height) = CardShadow.Render(card.Width, card.Height, _corner, Scale);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }
        bitmap.Invalidate();
        _shadow.Source = bitmap;
        _shadow.Width = width / Scale;
        _shadow.Height = height / Scale;
    }

    /// <summary>Rounds what the card holds, the views' full-bleed bars included.</summary>
    private void Clip(Size card)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Card);
        var compositor = visual.Compositor;
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new System.Numerics.Vector2((float)card.Width, (float)card.Height);
        geometry.CornerRadius = new System.Numerics.Vector2((float)_corner, (float)_corner);
        visual.Clip = compositor.CreateGeometricClip(geometry);
    }

    private void ThemeChanged()
    {
        Ui.IsDark = _root.ActualTheme != ElementTheme.Light;
        Ui.Scale = Scale;
        Restyle();
        ThemeFlipped?.Invoke();
    }

    /// <summary>
    /// Claudy's glass: a body that follows the wallpaper little, a light sheen from the top-left
    /// corner, and a coral halo there; then a hairline lit from the same corner.
    /// </summary>
    private void Restyle()
    {
        var corner = new CornerRadius(_corner);
        foreach (var layer in new[] { _glass, _sheen, _glow, _hairline, _strain })
        {
            layer.CornerRadius = corner;
        }
        var dark = Ui.IsDark;
        // Opaque: without Claudy's blur, what lies behind would show through crisp, not frosted.
        _glass.Background = Ui.Brush(dark ? new Rgba(0x1E, 0x1D, 0x1C) : new Rgba(0xF3, 0xF1, 0xEF));

        var white = new Rgba(255, 255, 255);
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        sheen.GradientStops.Add(new GradientStop { Color = Ui.Color(white, dark ? 0.10 : 0.30), Offset = 0 });
        sheen.GradientStops.Add(new GradientStop { Color = Ui.Color(white, dark ? 0.015 : 0.06), Offset = 1 });
        _sheen.Background = sheen;

        var coral = Theme.Color(Accent.Coral);
        var glow = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(0, 0),
            GradientOrigin = new Point(0, 0),
            RadiusX = 240,
            RadiusY = 240,
        };
        glow.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0.16), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0), Offset = 1 });
        _glow.Background = glow;

        var edge = dark ? white : new Rgba(0, 0, 0);
        var hairline = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        hairline.GradientStops.Add(new GradientStop { Color = Ui.Color(edge, dark ? 0.24 : 0.10), Offset = 0 });
        hairline.GradientStops.Add(new GradientStop { Color = Ui.Color(edge, dark ? 0.05 : 0.04), Offset = 1 });
        _hairline.BorderBrush = hairline;
        _shadowKey = default;
    }

    private void Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!IsDraggable || !args.GetCurrentPoint(Card).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressCursor = Cursor();
        _pressWindow = AppWindow.Position;
    }

    private void Moved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (_pressCursor is not { } press)
        {
            return;
        }
        // A release Windows delivered elsewhere ends the press: the card never follows a pointer
        // whose button is up.
        if (!args.GetCurrentPoint(Card).Properties.IsLeftButtonPressed)
        {
            EndDrag(args.Pointer);
            return;
        }
        var cursor = Cursor();
        var dx = cursor.X - press.X;
        var dy = cursor.Y - press.Y;
        if (!_isDragging)
        {
            if (Math.Sqrt((dx * dx) + (dy * dy)) < DragThreshold * Scale)
            {
                return;
            }
            // Taking the pointer from the view under it ends its press without a click: dropping
            // the card must not also switch its mode.
            _isDragging = true;
            Card.CapturePointer(args.Pointer);
        }
        AppWindow.Move(new PointInt32(_pressWindow.X + dx, _pressWindow.Y + dy));
        args.Handled = true;
    }

    private void Released(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (_isDragging)
        {
            args.Handled = true;
        }
        EndDrag(args.Pointer);
    }

    /// <summary>The bottom-right corner of the card where it was dropped becomes its anchor.</summary>
    private void EndDrag(Microsoft.UI.Xaml.Input.Pointer pointer)
    {
        _pressCursor = null;
        if (!_isDragging)
        {
            return;
        }
        _isDragging = false;
        Card.ReleasePointerCapture(pointer);
        var inset = (int)Math.Round(_inset * Scale);
        var size = AppWindow.Size;
        var position = AppWindow.Position;
        Anchor = new PointInt32(position.X + size.Width - inset, position.Y + size.Height - inset);
    }

    protected bool IsAdjusting => _isAdjusting;

    private static PointInt32 Cursor() => PanelChrome.Cursor();
}
