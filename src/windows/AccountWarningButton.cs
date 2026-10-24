using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexAccountDesk;

internal sealed class AccountWarningButton : Button
{
    public AccountWarningButton(string accountName, string details, Burn0Theme theme, Action? relogin = null)
    {
        Name = "AccountWarningButton"; Width = 20; Height = 20; MinWidth = MinHeight = 0;
        Padding = new Thickness(2); Margin = new Thickness(0, 0, 4, 0); VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent; Cursor = Cursors.Hand; SetResourceReference(StyleProperty, typeof(Button));
        Content = new Path { Data = Geometry.Parse("M12 3L22 21H2Z M12 9V14 M12 17V17.1"),
            Stroke = theme.Color("warning"), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 14, Height = 14, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        var tip = new ToolTip { Content = details + (relogin == null ? "" : "\n" + Loc.T("account.relogin")), Style = ToolTipTheme.StyleFor(theme), PlacementTarget = this, Placement = PlacementMode.Bottom };
        ToolTip = tip; ToolTipService.SetIsEnabled(this, false);
        AutomationProperties.SetName(this, accountName + ": " + Loc.T(relogin == null ? "갱신 필요" : "account.relogin")); AutomationProperties.SetHelpText(this, details);
        var pinned = false;
        void Close() { pinned = false; tip.IsOpen = false; }
        MouseEnter += (_, _) => tip.IsOpen = true;
        MouseLeave += (_, _) => { if (!pinned) tip.IsOpen = false; };
        Click += (_, _) => { if (relogin != null) { Close(); relogin(); } else { pinned = !pinned; tip.IsOpen = pinned; } };
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
        LostKeyboardFocus += (_, _) => Close();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Close(); };
        Window? owner = null;
        EventHandler deactivate = (_, _) => Close();
        Loaded += (_, _) => { owner = Window.GetWindow(this); if (owner != null) owner.Deactivated += deactivate; };
        Unloaded += (_, _) => { Close(); if (owner != null) owner.Deactivated -= deactivate; owner = null; };
    }
}
