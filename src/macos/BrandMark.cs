using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexAccountDesk;

internal class BrandMark : Control
{
    internal static readonly Geometry Shape = ReadShape();
    public IBrush Ink { get; set; } = Brushes.White;
    private static Geometry ReadShape()
    {
        using var stream = typeof(BrandMark).Assembly.GetManifestResourceStream("CodexAccountDesk.Mark.path")!;
        using var reader = new StreamReader(stream);
        return Geometry.Parse("F0 " + reader.ReadToEnd());
    }
    public override void Render(DrawingContext context)
    {
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 1000, Bounds.Height / 1000)))
            context.DrawGeometry(Ink, null, Shape);
    }
}
