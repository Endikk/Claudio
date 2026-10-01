using System.Runtime.InteropServices.WindowsRuntime;
using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Claudio.App.Views;

/// <summary>
/// SwiftUI's <c>.shadow(color:radius:y:)</c> for a rounded shape, as Claudy's <c>OutlineShadow</c>
/// draws it from the outline: a pre-blurred image laid under the shape and overflowing it. WinUI
/// has no coloured blur of its own, so the blur is computed once per size by <see cref="CardShadow"/>.
/// </summary>
internal static class OutlineShadow
{
    /// <summary>The reach of a blur past the shape: three deviations plus the offset.</summary>
    public static double Margin(double blur, double offset) => Math.Ceiling((3 * blur) + Math.Abs(offset));

    /// <summary>An image of the glow of a <paramref name="width"/> × <paramref name="height"/> shape, to lay under it.</summary>
    public static Image Under(double width, double height, double corner, Rgba colour, double blur, double offsetY, double scale)
    {
        var margin = Margin(blur, offsetY);
        var image = new Image
        {
            Stretch = Stretch.Fill,
            Width = width + (2 * margin),
            Height = height + (2 * margin),
            Margin = new Thickness(-margin),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        image.Source = Bitmap(CardShadow.Render(width, height, corner, scale, [new ShadowLayer(colour, 0, offsetY, blur)], margin, hollow: false));
        return image;
    }

    /// <summary>
    /// The glow of a capsule of any length, for a gauge's fill that grows and shrinks: drawn once
    /// for a short capsule, its round ends kept and its middle stretched (a nine-grid image).
    /// </summary>
    public static Image Stretchable(double height, Rgba colour, double blur, double offsetY, double scale)
    {
        var margin = Margin(blur, offsetY);
        var length = height * 3;
        var image = new Image
        {
            Stretch = Stretch.Fill,
            Height = height + (2 * margin),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-margin, -margin, 0, -margin),
        };
        var (pixels, width, rows) = CardShadow.Render(length, height, height / 2, scale, [new ShadowLayer(colour, 0, offsetY, blur)], margin, hollow: false);
        image.Source = Bitmap((pixels, width, rows));
        var edge = Math.Round((margin + (height / 2)) * scale);
        image.NineGrid = new Thickness(edge, 0, edge, 0);
        return image;
    }

    /// <summary>
    /// <paramref name="target"/> with its glow under it, redrawn whenever it changes size: the
    /// sign-in button's, the avatar's. A negative <paramref name="corner"/> means a capsule.
    /// </summary>
    public static Grid Wrap(FrameworkElement target, double corner, Rgba colour, double blur, double offsetY)
    {
        var host = new Grid { HorizontalAlignment = target.HorizontalAlignment, VerticalAlignment = target.VerticalAlignment };
        Image? glow = null;
        var drawn = (Width: 0.0, Height: 0.0, Scale: 0.0);
        target.SizeChanged += (_, _) =>
        {
            var size = (target.ActualWidth, target.ActualHeight, Ui.Scale);
            if (size == drawn || size.ActualWidth <= 0)
            {
                return;
            }
            drawn = size;
            if (glow is not null)
            {
                host.Children.Remove(glow);
            }
            var radius = corner < 0 ? Math.Min(size.ActualWidth, size.ActualHeight) / 2 : corner;
            glow = Under(size.ActualWidth, size.ActualHeight, radius, colour, blur, offsetY, Ui.Scale);
            host.Children.Insert(0, glow);
        };
        host.Children.Add(target);
        return host;
    }

    public static WriteableBitmap Bitmap((byte[] Pixels, int Width, int Height) image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(image.Pixels);
        }
        bitmap.Invalidate();
        return bitmap;
    }
}
