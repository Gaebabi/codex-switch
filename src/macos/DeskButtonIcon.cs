using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexAccountDesk;

internal enum DeskButtonIconKind { More, Refresh, Plus, Up, Down }

internal sealed class DeskButtonIcon : Control
{
    private static readonly Geometry Refresh = Geometry.Parse("M20 11A8.1 8.1 0 0 0 4.5 9M4 4V9H9M4 13A8.1 8.1 0 0 0 19.5 15M20 20V15H15");
    private static readonly Geometry Plus = Geometry.Parse("M12 5V19M5 12H19");
    private static readonly Geometry Up = Geometry.Parse("M12 19V5M5 12L12 5L19 12");
    private static readonly Geometry Down = Geometry.Parse("M12 5V19M5 12L12 19L19 12");
    private readonly DeskButtonIconKind _kind;
    private readonly IBrush _brush;
    public DeskButtonIcon(DeskButtonIconKind kind, IBrush brush) { _kind = kind; _brush = brush; IsHitTestVisible = false; Width = Height = 14; }
    public override void Render(DrawingContext context)
    {
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
        {
            if (_kind == DeskButtonIconKind.More)
            {
                foreach (var x in new[] { 5, 12, 19 }) context.DrawEllipse(_brush, null, new Point(x, 12), 1.5, 1.5);
            }
            else context.DrawGeometry(null, new Pen(_brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                _kind == DeskButtonIconKind.Refresh ? Refresh : _kind == DeskButtonIconKind.Plus ? Plus : _kind == DeskButtonIconKind.Up ? Up : Down);
        }
    }
}
