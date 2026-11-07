using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace CodexAccountDesk;

internal sealed class HardwareUsagePanel : Grid
{
    private readonly TextBlock[] _labels = new TextBlock[4], _values = new TextBlock[4], _temperatures = new TextBlock[4];
    private readonly Grid[] _cells = new Grid[4];
    private readonly ResourceTrendSurface[] _graphs = new ResourceTrendSurface[4];
    private readonly ResourcePressure[] _pressures = new ResourcePressure[4];
    private readonly ResourcePressure[] _temperaturePressures = new ResourcePressure[4];
    private readonly Button _cpuSetup = new() { Name = "CpuTemperatureSetupButton", Width = 32, Height = 16, MinWidth = 0, MinHeight = 0,
        Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
        BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
    private bool _cpuBusy;
    public event Action? CpuTemperatureRequested;
    public HardwareUsagePanel()
    {
        Height = 18; VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Stretch;
        for (var index = 0; index < 4; index++) ColumnDefinitions.Add(new());
        var names = new[] { "CPU", "MEM", "GPU", "VRAM" };
        for (var index = 0; index < 4; index++)
        {
            var cell = new Grid { Margin = new Thickness(0, 0, index < 3 ? 4 : 0, 0) };
            var content = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 4, 0) };
            content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            if (index == 0) content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var primary = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _graphs[index] = new(); cell.Children.Add(_graphs[index]); cell.Children.Add(content);
            _labels[index] = new() { Text = names[index], FontSize = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _values[index] = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _temperatures[index] = new() { FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            primary.Children.Add(_labels[index]); primary.Children.Add(_values[index]); content.Children.Add(primary); SetColumn(_temperatures[index], 1); content.Children.Add(_temperatures[index]); _cells[index] = cell;
            SetColumn(cell, index); Children.Add(cell);
            if (index == 0)
            {
                SetColumn(_cpuSetup, 2); content.Children.Add(_cpuSetup);
                _cpuSetup.Click += (_, _) => CpuTemperatureRequested?.Invoke();
                var menu = new ContextMenu();
                menu.Opened += (_, _) =>
                {
                    menu.Items.Clear(); var enable = new MenuItem { Header = Loc.T("thermal.cpu_enable") };
                    enable.Click += (_, _) => CpuTemperatureRequested?.Invoke(); menu.Items.Add(enable);
                };
                cell.ContextMenu = menu;
            }
        }
    }
    public void SetCpuTemperatureBusy(bool busy) { _cpuBusy = busy; _cpuSetup.IsEnabled = !busy; }
    public void Refresh(HardwareUsage usage, Burn0Theme theme)
    {
        SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, theme.Font);
        var readings = new[] { usage.Cpu, usage.Memory, usage.Gpu, usage.Vram };
        var now = DateTimeOffset.UtcNow;
        var scopes = new[] { "PC 전체 CPU", "PC 전체 물리 메모리", "가장 바쁜 GPU 엔진", "가장 높은 전용 GPU 메모리 사용률" };
        for (var index = 0; index < 4; index++)
        {
            var reading = readings[index]; var pressure = HardwareMath.Pressure(reading.Percent, _pressures[index]); _pressures[index] = pressure;
            var series = (usage.History ?? Array.Empty<HardwareObservation>()).Select(point => new ResourceTrendPoint(point.At,
                index switch { 0 => point.Cpu, 1 => point.Memory, 2 => point.Gpu, _ => point.Vram })).ToArray();
            _graphs[index].Refresh(series, reading.Percent, pressure, theme);
            _labels[index].Foreground = theme.Color("muted");
            _values[index].Foreground = theme.Color(pressure switch { ResourcePressure.Critical => "danger", ResourcePressure.Warning => "warning", ResourcePressure.Unknown => "muted", _ => "text" });
            _values[index].Text = pressure == ResourcePressure.Unknown ? "—" : reading.Percent!.Value.ToString("0", Loc.Culture) + "%";
            _temperatures[index].Text = TemperaturePresentation.Label(reading.Temperature, now);
            var temperatureKind = (TemperatureKind)index;
            _temperaturePressures[index] = TemperatureColors.Pressure(reading.Temperature, temperatureKind, _temperaturePressures[index], now);
            _temperatures[index].Foreground = theme.Color(TemperatureColors.Color(_temperaturePressures[index]));
            if (index == 0)
            {
                var needsSetup = reading.Temperature?.State is TemperatureState.ProviderMissing or TemperatureState.AccessDenied;
                _cpuSetup.Visibility = needsSetup ? Visibility.Visible : Visibility.Collapsed;
                _temperatures[index].Visibility = needsSetup ? Visibility.Collapsed : Visibility.Visible;
                _cpuSetup.IsEnabled = !_cpuBusy;
                var action = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var icon = reading.Temperature?.Code == "thermal.cpu_bundle_missing" ? "help" : reading.Temperature?.State == TemperatureState.AccessDenied ? "lock" : "download";
                action.Children.Add(Burn0Theme.Icon(icon, theme.Color("action"), 10));
                action.Children.Add(new TextBlock { Text = "°C", FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                _cpuSetup.Content = action;
                _cpuSetup.Background = theme.Color("hover"); _cpuSetup.Foreground = theme.Color("action");
                AutomationProperties.SetName(_cpuSetup, Loc.T("thermal.cpu_enable"));
                ToolTipTheme.Resource(_cpuSetup, "CPU", Loc.T("thermal.cpu_enable"), Loc.T("thermal.cpu_menu_hint"), theme, "action");
            }
            var tooltip = Loc.T(scopes[index]);
            if (pressure == ResourcePressure.Unknown) tooltip += "\n" + Loc.T(reading.Problem ?? "사용률 측정 중…");
            if (index == 0) tooltip += "\n" + Loc.T("thermal.cpu_menu_hint");
            else
            {
                if (reading.Device != null) tooltip += "\n" + reading.Device;
                if (reading.UsedBytes != null && reading.TotalBytes != null) tooltip += "\n" + Loc.F("{0:0.##} / {1:0.##} GiB", reading.UsedBytes.Value / 1073741824.0, reading.TotalBytes.Value / 1073741824.0);
                if (index >= 2)
                    foreach (var adapter in usage.Adapters)
                    {
                        var percentage = index == 2 ? adapter.Percent : adapter.UsedBytes == null ? null : HardwareMath.Percentage(adapter.UsedBytes.Value, adapter.Capacity);
                        tooltip += "\n" + adapter.Name + "  " + (percentage.HasValue ? percentage.Value.ToString("0.0", Loc.Culture) + "%" : "—");
                    }
            }
            tooltip += "\n" + Loc.F("2초 간격\n경고 {0}% / 위험 {1}%\n해제 기준 {2}%p 낮음", HardwareMath.WarningPercent, HardwareMath.CriticalPercent, HardwareMath.ReleaseMargin);
            tooltip += "\n" + (_graphs[index].SampleCount < 2 ? Loc.T("그래프 수집 시작…") : Loc.F("최근 {0:0}초 수집\n0–100% 고정 범위", _graphs[index].DurationSeconds));
            tooltip += "\n\n" + TemperaturePresentation.Tooltip(reading.Temperature, now);
            tooltip += "\n" + TemperatureColors.Tooltip(reading.Temperature, temperatureKind);
            ToolTipTheme.Resource(_cells[index], _labels[index].Text, pressure == ResourcePressure.Unknown ? "—" : reading.Percent!.Value.ToString("0.0", Loc.Culture) + "%", tooltip, theme,
                pressure switch { ResourcePressure.Critical => "danger", ResourcePressure.Warning => "warning", ResourcePressure.Unknown => "muted", _ => "text" });
        }
    }
    internal object Probe() => new { width = ActualWidth, height = ActualHeight,
        readings = _values.Select((value, index) => new { label = _labels[index].Text, text = value.Text, temperature = _temperatures[index].Text, temperaturePressure = _temperaturePressures[index].ToString(), temperatureColor = _temperatures[index].Foreground.ToString(), pressure = _pressures[index].ToString(), color = value.Foreground.ToString(),
            graphPercent = _graphs[index].Percent, graphSamples = _graphs[index].SampleCount, graphSegments = _graphs[index].SegmentCount,
            graphSeconds = _graphs[index].DurationSeconds, graphWidth = _graphs[index].ActualWidth,
            fits = value.DesiredSize.Width + _labels[index].DesiredSize.Width + (_temperatures[index].IsVisible ? _temperatures[index].DesiredSize.Width : 0) + 9 + (index == 0 && _cpuSetup.IsVisible ? 35 : 0) <= _cells[index].ActualWidth + 0.5 }).ToArray() };
}
