using System;

namespace CodexAccountDesk;

// Calendar ends are exclusive, so consecutive Korean days never overlap.
internal sealed record HistoryRange(DateTimeOffset Start, DateTimeOffset End, bool IncludesEnd = false)
{
    public static HistoryRange Resolve(int hours, HistoryRange? custom, DateTimeOffset now) =>
        custom == null ? new(now.AddHours(-Math.Clamp(hours, 1, 720)), now, true) :
        custom.End > now ? new(custom.Start, now, true) : custom;

    public static bool TryDates(DateTime? from, DateTime? through, DateTimeOffset now, out HistoryRange? range)
    {
        range = null;
        if (!from.HasValue || !through.HasValue || from.Value.Date > through.Value.Date ||
            from.Value.Date == DateTime.MinValue.Date || through.Value.Date > UsagePresentation.KoreanTime(now).Date || through.Value.Date == DateTime.MaxValue.Date) return false;
        var offset = TimeSpan.FromHours(9);
        range = new(new(DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Unspecified), offset),
            new(DateTime.SpecifyKind(through.Value.Date.AddDays(1), DateTimeKind.Unspecified), offset));
        return true;
    }

    public bool Contains(DateTimeOffset at) => at >= Start && (at < End || IncludesEnd && at == End);
}
