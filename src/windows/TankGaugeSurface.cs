using System;
using System.Windows;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class TankGaugeSurface : FrameworkElement
{
    private readonly Burn0Theme _theme;
    private readonly Brush _waterBrush;
    private Window? _host;
    private bool _rendering;
    private double _phase;
    private TimeSpan _lastFrame;
    public double? Remaining { get; }
    public bool Active { get; }
    internal bool AnimationRunning => _rendering;
    internal long AnimationFrames { get; private set; }
    public TankGaugeSurface(Burn0Theme theme, double? remaining, bool active = false)
    {
        _theme = theme; Remaining = remaining; Active = active; IsHitTestVisible = false;
        _waterBrush = theme.Color(remaining < 15 ? "warning" : "accent").Clone(); _waterBrush.Opacity = theme.Dark ? 0.20 : 0.15; _waterBrush.Freeze();
        Loaded += (_, _) => { _host = Window.GetWindow(this); if (_host != null) _host.StateChanged += HostStateChanged; UpdateAnimation(); };
        Unloaded += (_, _) => { StopAnimation(); if (_host != null) _host.StateChanged -= HostStateChanged; _host = null; };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }
    private void HostStateChanged(object? sender, EventArgs arguments) => UpdateAnimation();
    private void UpdateAnimation()
    {
        var animate = Active && Remaining is > 0 and < 100 && IsLoaded && IsVisible && _host?.WindowState != WindowState.Minimized;
        if (animate && !_rendering) { CompositionTarget.Rendering += RenderFrame; _rendering = true; }
        else if (!animate) StopAnimation();
    }
    private void StopAnimation() { if (_rendering) { CompositionTarget.Rendering -= RenderFrame; _rendering = false; } }
    private void RenderFrame(object? sender, EventArgs arguments)
    {
        var time = ((RenderingEventArgs)arguments).RenderingTime;
        if (time - _lastFrame < TimeSpan.FromMilliseconds(50)) return;
        _lastFrame = time; _phase = time.TotalSeconds * Math.Tau / 7; AnimationFrames++; InvalidateVisual();
    }
    internal static double LevelY(double height, double remaining) => height * (1 - Math.Clamp(remaining, 0, 100) / 100);
    internal static double SurfaceY(double height, double remaining, double position, bool active, double phase)
    {
        var y = LevelY(height, remaining);
        if (!active) return y;
        var amplitude = Math.Min(3, Math.Min(y, height - y) * 0.5);
        return y + amplitude * (Math.Sin(position * Math.Tau + phase) + 0.2 * Math.Sin(position * Math.Tau * 2 - phase * 0.7)) / 1.2;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var outline = new RectangleGeometry(bounds, _theme.Number("radius"), _theme.Number("radius"));
        drawing.DrawGeometry(_theme.Color("surface"), null, outline);
        if (Remaining == null || Remaining <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        drawing.PushClip(outline);
        if (Remaining >= 100) drawing.DrawRectangle(_waterBrush, null, bounds);
        else if (!Active) drawing.DrawRectangle(_waterBrush, null, new Rect(0, LevelY(ActualHeight, Remaining.Value), ActualWidth, ActualHeight - LevelY(ActualHeight, Remaining.Value)));
        else
        {
            var wave = new Point[49];
            for (var step = 0; step <= 48; step++)
            {
                var x = ActualWidth * step / 48;
                wave[step] = new Point(x, SurfaceY(ActualHeight, Remaining.Value, step / 48.0, true, _phase));
            }
            var water = new StreamGeometry();
            using (var path = water.Open())
            {
                path.BeginFigure(new Point(0, ActualHeight), true, true);
                path.PolyLineTo(wave, true, false); path.LineTo(new Point(ActualWidth, ActualHeight), true, false);
            }
            water.Freeze(); drawing.DrawGeometry(_waterBrush, null, water);
        }
        drawing.Pop();
    }
}
