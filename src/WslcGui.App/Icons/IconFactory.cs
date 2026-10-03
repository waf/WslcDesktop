using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace WslcGui.App.Icons;

/// <summary>Creates icon elements from <see cref="IconData"/> path data, colored with the inherited foreground.</summary>
internal static class IconFactory
{
    public static PathGeometry Geometry(string pathData) => PathGeometry.Parse(pathData);

    public static PathShape Create(string pathData, double size = 16)
    {
        var icon = new PathShape
        {
            Data = Geometry(pathData),
            Stretch = Stretch.Uniform,
        };
        icon.Bind(
            Shape.FillProperty,
            icon,
            TextElement.ForegroundProperty,
            static (Color color) => (Brush)new SolidColorBrush(color));
        return icon.Size(size, size);
    }

    /// <summary>
    /// An icon moved down by <paramref name="offsetY"/> pixels inside a box of the same size, for lining it up with
    /// text: a text block's glyphs sit lower than its box center, and flat icons look high next to lowercase text.
    /// </summary>
    public static Canvas CreateNudged(string pathData, double size, double offsetY) =>
        new Canvas().Size(size, size).Children(Create(pathData, size).CanvasTop(offsetY));
}
