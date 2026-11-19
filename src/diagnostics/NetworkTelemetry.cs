using System.Diagnostics.Tracing;

namespace CodexSwitch.Diagnostics;

internal sealed class NetworkTelemetry(Action<object> emit) : EventListener
{
    private static readonly AsyncLocal<string?> Request = new();
    internal static IDisposable Correlate(string id) { var previous = Request.Value; Request.Value = id; return new Scope(previous); }
    private sealed class Scope(string? previous) : IDisposable { public void Dispose() => Request.Value = previous; }
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Http" or "System.Net.NameResolution" or "System.Net.Sockets" or "System.Net.Security")
            EnableEvents(source, EventLevel.Informational, EventKeywords.All);
    }
    protected override void OnEventWritten(EventWrittenEventArgs value)
    {
        var numbers = new Dictionary<string, object>();
        if (value.Payload != null && value.PayloadNames != null)
            for (var i = 0; i < value.Payload.Count; i++)
                if (value.Payload[i] is byte or short or int or long or uint or ulong or double or float)
                    numbers[value.PayloadNames[i]] = value.Payload[i]!;
        // URLs, hostnames, socket addresses and all string payloads are deliberately excluded.
        emit?.Invoke(new { kind = "network_phase", relayRequest = Request.Value, source = value.EventSource.Name, phase = value.EventName,
            activity = value.ActivityId, relatedActivity = value.RelatedActivityId, numbers });
    }
}
