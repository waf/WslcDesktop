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
}
