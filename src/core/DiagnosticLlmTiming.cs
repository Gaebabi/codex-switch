using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal sealed class DiagnosticLlmTiming(string key, double start, bool precise)
{
    internal string Key = key;
    internal double Start = start;
    internal bool Precise = precise, Ambiguous;
    internal string? Response;
    internal double? FirstText, FirstOutput, LastOutput, End;
    internal long? OutputTokens, ReasoningTokens;
    internal int Chunks;
    private readonly List<double> _gaps = new();
    internal double? TtftMs => Precise && !Ambiguous && FirstText.HasValue && FirstText.Value >= Start ? FirstText - Start : null;
    internal long? DeliveredTokens => OutputTokens.HasValue && ReasoningTokens.HasValue && OutputTokens.Value >= ReasoningTokens.Value ? OutputTokens - ReasoningTokens : null;
    internal double? EndToEndTps => Precise && !Ambiguous && DeliveredTokens.HasValue && End.HasValue && End.Value > Start ? DeliveredTokens * 1000d / (End - Start) : null;
    // Token batches hide the first batch's generation interval. Never label this as exact server decoding speed.
    internal double? StreamTpsApprox => Precise && !Ambiguous && Chunks >= 2 && DeliveredTokens.HasValue && End.HasValue && FirstOutput.HasValue && End.Value > FirstOutput.Value ? DeliveredTokens * 1000d / (End - FirstOutput) : null;
    internal double? MaxGapMs => _gaps.Count == 0 ? null : _gaps.Max();
    internal double? P95GapMs => _gaps.Count == 0 ? null : _gaps.OrderBy(x => x).ElementAt(Math.Max(0, (int)Math.Ceiling(_gaps.Count * .95) - 1));
    internal void Delta(double at, bool text)
    {
        if (at < Start || LastOutput.HasValue && LastOutput.Value > at) { Ambiguous = true; return; }
        FirstOutput ??= at; if (text) FirstText ??= at;
        if (LastOutput.HasValue) _gaps.Add(at - LastOutput.Value);
        LastOutput = at; Chunks++;
    }
    internal DiagnosticLlmTiming Snapshot()
    {
        var copy = new DiagnosticLlmTiming(Key, Start, Precise) { Ambiguous = Ambiguous, Response = Response, FirstText = FirstText, FirstOutput = FirstOutput, LastOutput = LastOutput, End = End, OutputTokens = OutputTokens, ReasoningTokens = ReasoningTokens, Chunks = Chunks };
        copy._gaps.AddRange(_gaps); return copy;
    }
}
