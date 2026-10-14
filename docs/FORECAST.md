# Quota forecast

The forecast estimates how long the **reported remaining quota** would last if recent measured usage continued. It does not predict when you will work again or change the reported balance.

- Observations stay separate by account, quota bucket and reset segment. The model uses the last 24 hours in the current segment.
- Fully observed 10-minute UTC blocks are built by linear interpolation between real observations no more than five minutes apart.
- Zero-growth blocks do not reduce the learned usage rate. Partially used blocks still include their internal pauses.
- At least three growth blocks and 0.1 percentage point of total growth are required. Stale observations (15 minutes), a reached reset or insufficient data suspend the forecast.
- The initial rate is the median of the first three growth blocks. Later changes use a bounded exponential update with `alpha = 1 - 2^(-1/4)`.

Forecasts are conditional estimates. A pause, model choice or workload change can make them inaccurate. Reset-time corrections do not create artificial new usage segments. See `src/shared/UsageAnalysis.cs` and its replay checks for the complete implementation; the original detailed formula is also available in [Korean](FORECAST.ko.md).
