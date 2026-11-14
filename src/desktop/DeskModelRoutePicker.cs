using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace CodexAccountDesk;

internal sealed class DeskModelRoutePicker : Grid
{
    private readonly ModelProviderManager _manager;
    private readonly DeskTheme _theme;
    private readonly Button _models;
    private readonly Action<Exception> _failed;
    private readonly Action? _selected, _manage;
    internal DeskModelRoutePicker(DeskTheme theme, ModelProviderManager manager, Action<Exception> failed, Action? selected = null, Action? manage = null)
    {
        _theme = theme; _manager = manager; _failed = failed; _selected = selected; _manage = manage;
        HorizontalAlignment = HorizontalAlignment.Left;
        _models = theme.IconButton(DeskButtonIconKind.Models, Loc.T("router.visible"), Open);
        _models.Width = _models.Height = 30; _models.Padding = new Thickness(6); _models.Name = "ModelRoutePicker";
        Children.Add(_models);
        _models.KeyDown += (_, args) => { if (args.Key == Key.Down) { Open(); args.Handled = true; } };
        Refresh();
    }
    private void Open()
    {
        var menu = _theme.Menu(_models);
        foreach (var provider in _manager.Settings.Providers)
        {
            menu.Items.Add(new MenuItem { Header = provider.Name, IsEnabled = false });
            foreach (var model in provider.Models)
            {
                var id = model.Alias(provider.Id);
                var item = new MenuItem { Header = model.UpstreamId, ToggleType = MenuItemToggleType.CheckBox, IsChecked = model.EnabledInCodex, StaysOpenOnClick = true };
                ToolTip.SetTip(item, Loc.T("router.visibility_help"));
                item.Click += async (_, _) =>
                {
                    var enabled = item.IsChecked; menu.IsEnabled = false;
                    try { await _manager.SetModelEnabledAsync(id, enabled, CancellationToken.None); _selected?.Invoke(); }
                    catch (Exception error) { _failed(error); }
                    finally
                    {
                        item.IsChecked = _manager.Settings.Providers.SelectMany(p => p.Models.Select(m => (p, m))).Single(pair => pair.m.Alias(pair.p.Id) == id).m.EnabledInCodex;
                        menu.IsEnabled = true; Refresh();
                    }
                };
                menu.Items.Add(item);
            }
        }
        if (_manage != null)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var settings = new MenuItem { Header = Loc.T("providers.title") };
            settings.Click += (_, _) => { menu.Close(); _manage(); }; menu.Items.Add(settings);
        }
        _models.ContextMenu = menu; menu.Open(_models);
    }
    internal void Refresh()
    {
        var settings = _manager.Settings; var count = settings.Providers.SelectMany(provider => provider.Models).Count(model => model.EnabledInCodex);
        IsVisible = settings.Providers.Count != 0 || _manage != null;
        _models.Content = new DeskButtonIcon(DeskButtonIconKind.Models, _theme.Brush(count == 0 ? "muted" : "text")) { Width = 18, Height = 18 };
        var label = Loc.F("router.visible_count", count);
        AutomationProperties.SetName(_models, label);
        ToolTip.SetTip(_models, label + "\n" + Loc.T("router.visibility_help") +
            (_manager.CatalogRefreshStatus.DisplayMessage is string status ? "\n" + status : ""));
    }
}
