using System;
using System.Linq;
using System.Text;

namespace CodexAccountDesk;

internal static class CreditPresentation
{
    public static string Key(CreditTrack track) => "credits:" + Convert.ToHexString(Encoding.UTF8.GetBytes(track.BucketId));
    public static bool Selected(string? key) => key?.StartsWith("credits:", StringComparison.Ordinal) == true && key.Length > 8 &&
        (key.Length - 8) % 2 == 0 && key[8..].All(Uri.IsHexDigit);
    public static CreditTrack Track(AccountProfile profile, string? key) =>
        profile.CreditHistory.FirstOrDefault(t => Key(t) == key) ?? profile.CreditHistory.FirstOrDefault() ??
        new() { BucketId = profile.Usage?.Buckets.FirstOrDefault()?.Id ?? "codex" };
    public static string Number(decimal? value)
    {
        if (!value.HasValue) return "—";
        if (value != 0 && value > -0.0001m && value < 0.0001m) return value.Value.ToString("G4", Loc.Culture);
        var text = value.Value.ToString("N4", Loc.Culture).TrimEnd('0');
        var separator = Loc.Culture.NumberFormat.NumberDecimalSeparator;
        return text.EndsWith(separator, StringComparison.Ordinal) ? text[..^separator.Length] : text;
    }
    public static string Balance(AccountProfile profile, CreditTrack track)
    {
        var buckets = profile.Usage?.Buckets.Where(b => b.Id == track.BucketId).Take(2).ToArray();
        return buckets?.Length != 1 ? "—" : buckets[0].UnlimitedCredits == true ? "∞" : Number(track.Points.LastOrDefault()?.Balance);
    }
    public static double Maximum(CreditObservation[] points)
    {
        var maximum = points.Where(p => p.ObservedDecrease.HasValue).Select(p => (double)p.ObservedDecrease!.Value).DefaultIfEmpty().Max();
        if (maximum <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        return Math.Ceiling(maximum / magnitude) * magnitude;
    }
    public static string Details(CreditObservation point) =>
        Loc.F("credits.reading", UsagePresentation.KoreanTime(point.At), Number(point.Balance), Number(point.ObservedDecrease)) + "\n" +
        (point.Since is DateTimeOffset since ? Loc.F("credits.interval", UsagePresentation.KoreanTime(since), UsagePresentation.KoreanTime(point.At)) : Loc.T("credits.unknown_interval"));
}
