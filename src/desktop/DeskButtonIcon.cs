using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexAccountDesk;

internal enum DeskButtonIconKind { More, Refresh, Plus, Up, Down, Sun, Moon, Launch, Settings, Eye, EyeOff, Copy, Models }

internal sealed class DeskButtonIcon : Control
{
    private static readonly Geometry Refresh = Geometry.Parse("M20 11A8.1 8.1 0 0 0 4.5 9M4 4V9H9M4 13A8.1 8.1 0 0 0 19.5 15M20 20V15H15");
    private static readonly Geometry Plus = Geometry.Parse("M12 5V19M5 12H19");
    private static readonly Geometry Up = Geometry.Parse("M12 19V5M5 12L12 5L19 12");
    private static readonly Geometry Down = Geometry.Parse("M12 5V19M5 12L12 19L19 12");
    private static readonly Geometry Sun = Geometry.Parse("M12 2V4M12 20V22M2 12H4M20 12H22M5 5L6.5 6.5M17.5 17.5L19 19M5 19L6.5 17.5M17.5 6.5L19 5M16 12A4 4 0 1 1 8 12A4 4 0 1 1 16 12");
    private static readonly Geometry Moon = Geometry.Parse("M20.5 13A8.5 8.5 0 1 1 11 3.5A6.5 6.5 0 0 0 20.5 13Z");
    private static readonly Geometry Launch = Geometry.Parse("M14 4H20V10M20 4L10 14M10 5H5V20H20V15");
    private static readonly Geometry Settings = Geometry.Parse("M9 3H15L16 6L19 7L22 10V14L19 17L16 18L15 21H9L8 18L5 17L2 14V10L5 7L8 6Z M15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12");
    private static readonly Geometry Eye = Geometry.Parse("M2 12C4 8 7 5 12 5S20 8 22 12C20 16 17 19 12 19S4 16 2 12Z M15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12");
    private static readonly Geometry EyeOff = Geometry.Parse("M2 12C4 8 7 5 12 5S20 8 22 12C20 16 17 19 12 19S4 16 2 12Z M15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12 M3 3L21 21");
    private static readonly Geometry Copy = Geometry.Parse("M9 9H21V21H9Z M15 9V3H3V15H9");
    private static readonly Geometry Models = Geometry.Parse("M4 5H20M4 12H20M4 19H20 M9 3V7M15 10V14M9 17V21");
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
                _kind switch { DeskButtonIconKind.Refresh => Refresh, DeskButtonIconKind.Plus => Plus, DeskButtonIconKind.Up => Up,
                    DeskButtonIconKind.Down => Down, DeskButtonIconKind.Sun => Sun, DeskButtonIconKind.Launch => Launch, DeskButtonIconKind.Settings => Settings,
                    DeskButtonIconKind.Eye => Eye, DeskButtonIconKind.EyeOff => EyeOff, DeskButtonIconKind.Copy => Copy, DeskButtonIconKind.Models => Models, _ => Moon });
        }
    }
}
