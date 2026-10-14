using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal static class AppIdentity
{
    private static readonly Geometry Mark = ReadGeometry("Mark.path");
    private static readonly Geometry Body = ReadGeometry("MarkBody.path");
    private static Geometry ReadGeometry(string resource)
    {
        using var stream = typeof(AppIdentity).Assembly.GetManifestResourceStream("CodexAccountDesk." + resource)!;
        using var reader = new StreamReader(stream);
        var geometry = Geometry.Parse("F0 " + reader.ReadToEnd()); geometry.Freeze(); return geometry;
    }
    public static FrameworkElement Glyph(Brush foreground, double size)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1000, 1000))));
        drawing.Children.Add(new GeometryDrawing(foreground, null, Mark)); drawing.Freeze();
        return new Image { Width = size, Height = size, Source = new DrawingImage(drawing), Stretch = Stretch.Uniform };
    }
    public static ImageSource WindowIcon
    {
        get
        {
            using var stream = typeof(AppIdentity).Assembly.GetManifestResourceStream("CodexAccountDesk.AppIcon.ico")!;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[decoder.Frames.Count - 1];
        }
    }
    public static void ExportIcons(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512, 1024 })
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(size / 1000d, size / 1000d));
                context.DrawGeometry(Brushes.White, null, Body);
                context.DrawGeometry(Brushes.Black, null, Mark); context.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, size + ".png")); encoder.Save(output);
        }
    }
    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = typeof(AppIdentity).Assembly.GetManifestResourceStream("CodexAccountDesk.AppIcon.ico")!;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
}
