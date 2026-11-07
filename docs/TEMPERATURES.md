# Resource and disk temperatures

Source **1.40.5** adds built-in Windows CPU collection to the Celsius gauges introduced in1.29.0. Utilization/free space remains on the left, with the same footer height. Values and sensor/access status appear in existing tooltips. Release1.40.7 Windows packages include this worker and visible download/authorization buttons. Older1.40.2 downloads retain the earlier external-provider CPU path.

Missing, unsupported, denied, invalid or stale readings display **—°C**, never a made-up zero or a percentage converted to Celsius. Temperature is independent of utilization warning colors. GPU core temperature does not substitute for VRAM. A reading older than 30 seconds is unavailable; this checks query age, not a provider's undocumented internal sample age.

Temperature text uses normal theme text, yellow caution and red high-temperature colors; unavailable values stay muted. Valid paired GPU/storage warning/critical thresholds reported by the device take precedence. Otherwise display defaults are CPU 75/90°C, RAM 60/75°C, GPU 80/90°C, VRAM 90/105°C and disk 55/70°C. These defaults are visual guidance, not a claim about a device's rated maximum. Thresholds and their source appear in the tooltip, and a three-degree release margin prevents rapid color changes near a threshold. A multi-device volume uses the worst device's own severity even when another disk supplies the highest displayed Celsius value. No control or alarm action is triggered.

| Gauge | Windows source | Scope |
| --- | --- | --- |
| CPU | Built-in sensor-only worker; existing WMI provider as fallback | AMD family17h/19h/1Ah Tctl or Intel family6 DTS package/core; one package, no motherboard substitute |
| MEM | Actual `Memory` sensors from that provider | Highest reported DIMM/memory temperature, if present |
| GPU | Read-only WDDM performance data, joined by actual DXGI LUID | Driver-dependent; unambiguous single physical adapter |
| VRAM | Existing NVIDIA NVML memory field through that adapter's PCI address, or compatible WMI memory channel | Many consumer drivers do not expose this sensor |
| Disk | Windows storage temperature property and actual volume disk extents | Device/driver/USB/RAID/permissions dependent |

Disk queries select the device's composite sensor when provided, otherwise the highest reported sensor. Multi-disk volumes show the highest available device temperature with coverage such as `1/2` and list missing devices in the tooltip. Drive letters are not assumed to be disk numbers. Free space may be available while temperature is unavailable.

Compatible providers are still read from `root/LibreHardwareMonitor` or `root/OpenHardwareMonitor` when already running. Some versions do not publish WMI. The [historical WMI schema](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.4/LibreHardwareMonitor/WMI) documents the protocol; it is not a recommendation to install that version. WMI remains optional for CPU and the existing MEM/GPU fallbacks. Built-in CPU setup below replaces the external-monitor requirement without changing fans, voltages or account settings.

## Built-in Windows CPU sensor

Use the complete Windows folder, including `sensors/CodexSwitch.Sensors.exe`. No third-party monitor application needs to run. The account app remains `asInvoker`; its separate **NativeAOT** worker has no account/vault/application dependency, CLR startup hooks, account arguments or input commands. It emits a bounded CPU temperature/status packet every two seconds over an output-only current-user pipe. Both endpoints verify the process ID; a nonce and parent start time bind its lifetime. Closing the app/pipe ends the worker. No Codex Switch service or automatic privileged startup is created.

**Select the CPU temperature button → Enable built-in CPU temperature.** A compact **download icon + °C** replaces unavailable Celsius when driver installation is needed. A **lock + °C** identifies session authorization for an installed driver; incomplete packages use a help icon. The action is visible without right-clicking and remains accessible by keyboard and screen reader. CPU's right-click menu remains available. Source1.40.7 re-probes capability before deciding to install: an already running driver is never reinstalled merely because the old worker began before installation. No app restart is needed to discover a driver installed while the app was open. Setup stays disabled while one action is pending, and the button disappears when a fresh temperature arrives.

If the driver is missing, an explicit confirmation offers the fixed official signed **PawnIO2.2.0** download. Its exact SHA-256 is verified while retaining a non-writable file handle before Windows UAC setup. The driver is not bundled/relicensed; it is shared with other software and never uninstalled by this app. After setup, authorize the sensor-only worker for this app session. An existing driver may need only worker approval. Declining UAC leaves temperatures unavailable and account features intact. No download, installation or UAC happens automatically. Installer exit3010 is reported as restart required without restarting Windows. Setup errors identify their bounded Windows code instead of suggesting an encrypted account-store failure. A normal unprivileged worker probes existing capability at startup; denied access stays denied until explicit authorization. Reopening the app may need another worker authorization; admin privilege is not silently retained.

Only two [pinned signed upstream modules with corresponding source/license](../src/sensors/modules/SOURCE.md) are embedded, without a full monitoring library. Fixed read entry points address AMD SMN `0x59800`, or Intel MSR `0x1A2` plus `0x1B1`/`0x19C`. The worker accepts no register addresses, module paths or control requests from the account app. AMD SMN reading selects a PCI address under `Global\Access_PCI`; this addressing write is not a fan/voltage/control change. Busy/abandoned bus access stays unavailable, without an unlocked retry. Intel target/status reads share a fixed CPU affinity restored afterward. No WinRing0, unrestricted PawnIO, test-signing, security exclusions or kernel-policy bypass is used.

The published account app embeds its worker's exact digest and retains a non-writable file handle while using it; a replacement is refused before launch/elevation. The worker is an unsigned project executable like the account app, so its UAC prompt may show an unknown publisher. The driver installer is the separately verified upstream signed executable. Authorization requires the same Windows user; credentials for a different administrator do not cross the current-user pipe boundary. One activation remains pending until the initial worker result, preventing duplicate authorization requests.

AMD reports **Tctl**, including control-temperature offsets on older Zen parts; it is not relabeled as die temperature. Intel requires a real target MSR and valid DTS bit, without guessed TjMax. Initial coverage is one CPU package, not hottest-of-all sockets/CCDs. Older CPUs, Windows ARM64, virtual machines, unsupported registers and blocked drivers can remain unavailable. GPU/VRAM/disk and nine language packs are retained. Physical CPU success requires a fresh native value; a running driver or fictional UI screenshot does not establish it.

Hosted runners expose varying virtual CPU models; some advertise DTS while rejecting its MSRs. The collector retains the actual bounded Win32 failure code; unsupported operations (1/50) are distinct from read failures. Hosted driver/signature/IPC/lifecycle tests do not require an accessible physical sensor. Their receipt explicitly separates `nativeSensorValueAccepted` from those integration checks and reports failures as failures. A hypervisor flag alone does not disable collection, because physical Windows hosts using VBS/Hyper-V can report it too. No VBS/Hyper-V or security policy is changed.

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
