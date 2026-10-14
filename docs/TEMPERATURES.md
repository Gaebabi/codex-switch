# Resource and disk temperatures

Source **1.29.0** adds Celsius values on the right of CPU/MEM/GPU/VRAM and disk gauges. Utilization/free space remains on the left, with the same footer height. Values and sensor/access status appear in existing tooltips. Current **1.27.6** binary downloads do not include this feature.

Missing, unsupported, denied, invalid or stale readings display **—°C**, never a made-up zero or a percentage converted to Celsius. Temperature is independent of utilization warning colors. GPU core temperature does not substitute for VRAM. A reading older than 30 seconds is unavailable; this checks query age, not a provider's undocumented internal sample age.

Temperature text uses normal theme text, yellow caution and red high-temperature colors; unavailable values stay muted. Valid paired GPU/storage warning/critical thresholds reported by the device take precedence. Otherwise display defaults are CPU 75/90°C, RAM 60/75°C, GPU 80/90°C, VRAM 90/105°C and disk 55/70°C. These defaults are visual guidance, not a claim about a device's rated maximum. Thresholds and their source appear in the tooltip, and a three-degree release margin prevents rapid color changes near a threshold. A multi-device volume uses the worst device's own severity even when another disk supplies the highest displayed Celsius value. No control or alarm action is triggered.

| Gauge | Windows source | Scope |
| --- | --- | --- |
| CPU | Existing compatible local WMI provider, `Hardware`/`Sensor` schema | Highest reported CPU sensor, excluding motherboard socket substitutes |
| MEM | Actual `Memory` sensors from that provider | Highest reported DIMM/memory temperature, if present |
| GPU | Read-only WDDM performance data, joined by actual DXGI LUID | Driver-dependent; unambiguous single physical adapter |
| VRAM | Existing NVIDIA NVML memory field through that adapter's PCI address, or compatible WMI memory channel | Many consumer drivers do not expose this sensor |
| Disk | Windows storage temperature property and actual volume disk extents | Device/driver/USB/RAID/permissions dependent |

Disk queries select the device's composite sensor when provided, otherwise the highest reported sensor. Multi-disk volumes show the highest available device temperature with coverage such as `1/2` and list missing devices in the tooltip. Drive letters are not assumed to be disk numbers. Free space may be available while temperature is unavailable.

Compatible providers are read from `root/LibreHardwareMonitor` or `root/OpenHardwareMonitor` when already running. Some versions do not publish WMI. The [historical WMI schema](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.4/LibreHardwareMonitor/WMI) documents the protocol; it is not a recommendation to install that version. Use maintained software and supported configuration. This app does not download, launch, install or elevate a sensor monitor, kernel driver or service, or change fans/voltages/settings. Its new Windows dependency, `System.Management`, is a managed WMI client.

NVML loads from the protected Windows system directory with restricted dependency search. Native handles/libraries are released. CPU/memory WMI reads run in the background every five seconds; missing providers are probed once a minute. Temperature queries do not access Codex accounts, credentials or chats.

VRAM matching enumerates NVIDIA PCI identities and requires a unique bus/device/function match for an NVIDIA DXGI adapter. It does not assume PCI domain zero or guess among identical addresses across domains. Unresolved/linked/multi-domain ambiguity remains unavailable.

**macOS:** right-aligned UI/status is present, but native temperature collection is not implemented. Values show `—°C`; thermal pressure is not converted into Celsius. Native Mac GUI and future sensor support require separate device verification.

## Verification

The production reader ran on one Windows host without administrator elevation, preserving all existing utilization readings. It returned GPU around 53°C and C:/D: backing-device composite readings around 71°C/49°C. CPU/RAM had no compatible provider; VRAM was driver-unsupported. This is one host observation, not universal coverage. [Program before/after and confirmation](evidence/temperatures/README.md)

```text
dotnet run --project tests/temperatures/CodexSwitch.Temperatures.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
python scripts/verify-localization.py
python scripts/render-demo.py --temperature-preview --output artifacts/temperature-ui.png
```

The 29 production contract checks cover malformed storage bytes/extents, unreported sentinel, mapping/partial coverage, zero/negative/invalid data, independent core/VRAM channels, ambiguous GPU names, stale values and nine languages. CI runs these portable checks on Windows, Ubuntu and both Mac architectures; it does not manufacture native availability.

`--temperature-preview` uses fictional sensor-capable data. `--live-resources` reads actual local hardware/disk values while account/session inputs remain fictional; neither starts Codex or uses a real vault/home. Inspect minimum 500×360 captures. JSON receipts remain ignored private verification material.

Primary API references: [storage descriptor](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_temperature_data_descriptor), [volume disk extents](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_volume_get_volume_disk_extents), [WDDM Celsius units](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdata), [WDDM device thresholds](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdatacaps), [NVML memory field](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlFieldValueEnums.html).
