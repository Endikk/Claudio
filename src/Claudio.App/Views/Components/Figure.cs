using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// A percentage set like display type, as Claudy's <c>Figure</c>: rounded, tight, its unit small
/// and quiet beside it. A window with no reading shows a dash, and no unit.
/// </summary>
internal sealed partial class Figure : Grid
{
    private readonly double _size;
    private readonly TextBlock _text = new();

    public Figure(double size = 44)
    {
        _size = size;
        Ui.Style(_text, size);
        // SF Rounded semibold is heavier than Nunito's: the bold weight carries the same mass.
        _text.FontWeight = Ui.Bold;
        _text.CharacterSpacing = -20; // -2 % of the size
        _text.TextWrapping = TextWrapping.NoWrap;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_text);
    }

    public void Update(UsageWindow window) =>
        Ui.Figure(_text, UsageFormat.Percent(window), window.IsMeasured ? Ui.Primary(1) : Ui.Tertiary, window.IsMeasured,
                  _size * 0.4, Ui.Secondary, Ui.Medium);
}
