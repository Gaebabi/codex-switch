# Quota forecast

The forecast estimates how long the **reported remaining quota** would last if recent measured usage continued. It does not predict when you will work again or change the reported balance.

- Observations stay separate by account, quota bucket and reset segment. The model uses the last 24 hours in the current segment.
- Fully observed 10-minute UTC blocks are built by linear interpolation between real observations no more than five minutes apart.
- Zero-growth blocks do not reduce the learned usage rate. Partially used blocks still include their internal pauses.
- At least three growth blocks and 0.1 percentage point of total growth are required. Stale observations (15 minutes), a reached reset or insufficient data suspend the forecast.
- The initial rate is the median of the first three growth blocks. Later changes use a bounded exponential update with `alpha = 1 - 2^(-1/4)`.

Forecasts are conditional estimates. A pause, model choice or workload change can make them inaccurate. Reset-time corrections do not create artificial new usage segments. See `src/core/UsageAnalysis.cs` and its replay checks for the complete implementation; the original detailed formula is also available in [Korean](FORECAST.ko.md).

Source 1.40.3 established the shared Avalonia history window's upward used-quota chart, KST labels, neutral range selection and reset/gap discontinuities. [Actual application evidence for that version](evidence/usage-history/README.md).

The pending History update adds a30-day preset and custom inclusive start/end dates in Korean time. When ordinary quota stays at100% and the matching recorded credit balance decreases, an orange **Credit range** continues above it. Its upper axis uses actual credit quantities on a separate scale; only the ordinary quota uses percentages. The metrics and tooltip separate observed consumption from any gap supplement. An estimate requires recorded balances at both ends and is drawn with a dashed orange line or outline bar. [Credit observation, recovery and gap rules](development/CREDIT_HISTORY.md).

The large balance remains **remaining quota**. Presets use the saved preference, while custom dates stay with the open History window through refresh and metric changes. Reset segments and collection gaps stay disconnected. These display changes do not alter recorded quota or the forecast model; a reset-first estimate remains explicit and missing/insufficient history stays unavailable.
