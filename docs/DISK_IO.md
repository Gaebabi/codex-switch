# Disk read/write gauges

Source1.40.6 adds **two3px vertical bars** to the right of each Windows disk temperature. Left is read, right is write. Hover the disk for exact binary-unit rates, separate read/write latency, busy time, device profile, scale and warning basis. Accessible names include direction/rate. The footer retains its24px height; free-space and temperature colors are independent.

`LogicalDisk(<actual volume>)` native English PDH counters provide read/write bytes per second, average seconds per operation and idle time. Collection runs in the existing background drive monitor every2seconds; free space/temperature retain their15second cadence. Device capabilities refresh every60seconds. The first two samples, unavailable counters, removed drives and stale values stay unknown rather than becoming idle zero. A real zero is displayed as zero. There is no benchmark, filesystem traversal, file-content access, new driver, administrator request or performance-counter enable/disable operation.

## Device characteristics and color

The actual volume's disk extents are resolved before querying `StorageDeviceSeekPenaltyProperty` and `StorageDeviceProperty`. A single consistent seek/no-seek profile is required across all backing devices. No model string, serial number or owner label is used to infer speed. Virtual/RAID/storage-space, incomplete, unsupported or mixed characteristics remain unknown/neutral. USB/SAS/SATA/NVMe can expose the relevant characteristics through their driver; that does not guarantee a peak bandwidth.

General Windows storage queries do **not** provide a reliable manufacturer-rated sequential throughput for every disk and bridge. Interface speed and maximum transfer *size* are not measured drive capacity. Bar height therefore uses the highest read/write rate observed in the last60seconds, with a1KiB/s floor. This scale is disclosed in the tooltip and is never called device utilization/rated capacity. A full bar alone does not change warning color.

| Actual capability | Warning / high read or write latency | Basis |
| --- | --- | --- |
| Seek penalty reported | 25 /50ms | General Windows storage guidance |
| No seek penalty reported | 10 /20ms | Conservative application policy for solid-state behavior; not a manufacturer limit |
| Unknown/mixed | None | No invented device threshold |

Each channel needs at least80% volume busy time **and** latency at/above its threshold continuously for60seconds. Short spikes remain neutral. A gap over10seconds, idle/no activity, capability change, missing latency/busy data or recovery clears the pressure interval. Read is normally blue and write green; sustained warning/high pressure is yellow/red. These colors indicate observed I/O pressure and possible responsiveness problems, **not disk damage, health or a prediction of failure**. Existing actual device-reported temperature limits remain separate.

## Verification boundaries

Portable tests cover capability byte bounds, mixed/unknown coverage, real zero, formatting, observed-scale expiry, independent persistent latency/busy gates, invalid/stale values and all nine catalogues. Native Windows tests read actual counters without generated I/O and validate range/availability; they do not benchmark physical disks. Hosted own-window screenshots use fictional rates/profile/latency to prove the actual UI states and minimum geometry. They must never be labeled as owner-device performance measurements. macOS/Linux throughput collection is not implemented by this change; their existing behavior stays intact.

Primary references: [device seek penalty](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_device_seek_penalty_descriptor), [disk performance counters](https://learn.microsoft.com/en-us/previous-versions/aa394308(v=vs.85)), [Windows storage latency guidance](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-performance-problems-in-windows#storage), [adapter maximum transfer length is bytes per operation](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_adapter_descriptor).
