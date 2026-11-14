using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class GatewayHistoryBridge
{
    private readonly GatewayHistoryStore _history;
    private readonly ModelProviderStore _providers;
    private readonly HttpClient _http;
    private readonly Func<string?> _home;
    private readonly Func<GatewayIdentity, bool> _stillSignedIn;
    private readonly SemaphoreSlim _summaries = new(1, 1);
    internal GatewayHistoryBridge(string directory, ModelProviderStore providers, HttpClient http, Func<string?> home, Func<GatewayIdentity, bool> stillSignedIn)
    { _history = new(directory); _providers = providers; _http = http; _home = home; _stillSignedIn = stillSignedIn; }
    internal void RememberOutput(string? session, GatewayIdentity identity, GatewayHistorySource source, JsonElement item)
    {
        if (JsonValue.String(item, "type") is "compaction" or "compaction_summary" or "context_compaction" &&
            JsonValue.String(item, "encrypted_content") is string ciphertext && !string.IsNullOrEmpty(session))
            _history.RememberSource(ciphertext, GatewayHistoryStore.Conversation(session, identity), source);
    }

    internal async Task PrepareAsync(GatewayHistoryInput input, string? session, GatewayIdentity identity, GatewayHistorySource target, ModelProviderSettings snapshot,
        HttpListenerRequest incoming, string operation, CancellationToken cancellation)
    {
        if (!input.Items.Any(GatewayHistoryInput.Compaction)) return;
        var conversation = GatewayHistoryStore.Conversation(session, identity);
        foreach (var item in input.Items.Where(GatewayHistoryInput.Compaction))
        {
            cancellation.ThrowIfCancellationRequested();
            var ciphertext = item.Ciphertext!; var portable = ciphertext.StartsWith(GatewayHistoryStore.Prefix, StringComparison.Ordinal);
            var key = portable ? ciphertext : GatewayHistoryStore.OpaqueKey(ciphertext);
            var checkpoint = _history.Read(key, conversation);
            if (checkpoint?.Summary is string summary) { input.Replacements[item.Index] = GatewayHistoryInput.SummaryItem(summary); continue; }
            if (portable) throw new GatewayHistoryException("This portable checkpoint is unavailable in the original Switch history store. Restore that protected store or create a readable handoff in the source chat; no history was removed.");
            var source = checkpoint?.Source;
            if (source == null && _home() is string home)
            {
                var local = LocalCompactionSources.Read(home, session, ciphertext, identity.AccountId);
                if (local != null && local.Model != ModelRouting.Model && !local.Model.StartsWith("cs_", StringComparison.Ordinal)) source = new("openai", local.Model);
            }
            if (source == null)
            {
                // The unchanged native route is the sole legacy inference supported by the old contract.
                if (target.Route == "openai" && !_providers.RequiresPortableHistory(session, identity, target.Route)) continue;
                throw new GatewayHistoryException(ModelHistoryHandoff.Refusal);
            }
            if (source.Route == target.Route && source.Model == target.Model) continue;
            await _summaries.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var requestId = RequestIdentity(operation, input, item.Index);
                var existing = _history.BeginSummary(key, conversation, source, requestId);
                summary = existing ?? await SummarizeAsync(input.Values(item.Index), source, snapshot, incoming, identity, cancellation, instructions: input.Instructions).ConfigureAwait(false);
                if (existing == null) _history.CompleteSummary(key, conversation, source, summary);
                input.Replacements[item.Index] = GatewayHistoryInput.SummaryItem(summary);
            }
            finally { _summaries.Release(); }
        }
    }

    internal async Task<string> CompactAsync(GatewayHistoryInput input, string? session, GatewayIdentity identity, GatewayHistorySource source, ModelProviderSettings snapshot,
        HttpListenerRequest incoming, string requestIdentity, CancellationToken cancellation)
    {
        var conversation = GatewayHistoryStore.Conversation(session, identity);
        var key = "summary:" + SecureFiles.Hash(conversation + "\n" + requestIdentity);
        await _summaries.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var summary = _history.BeginSummary(key, conversation, source, requestIdentity);
            if (summary == null)
            {
                summary = await SummarizeAsync(input.Values(), source, snapshot, incoming, identity, cancellation, input.RawInput, input.Instructions).ConfigureAwait(false);
                _history.CompleteSummary(key, conversation, source, summary);
            }
            return _history.CreatePortable(conversation, source, summary);
        }
        finally { _summaries.Release(); }
    }

    private async Task<string> SummarizeAsync(IEnumerable<ReadOnlyMemory<byte>> history, GatewayHistorySource source, ModelProviderSettings snapshot,
        HttpListenerRequest incoming, GatewayIdentity identity, CancellationToken cancellation, ReadOnlyMemory<byte> rawInput = default, ReadOnlyMemory<byte> instructions = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try { return await SummarizeCoreAsync(history, source, snapshot, incoming, identity, deadline.Token, rawInput, instructions).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new GatewayHistoryException("The source model did not finish the context handoff within three minutes. The original history was preserved; a new explicit turn can retry. No destination request was sent."); }
        catch (HttpRequestException)
        { throw new GatewayHistoryException("The source model connection failed during context handoff. The original history was preserved and no destination request was sent. Check the source connection before a new explicit turn."); }
    }
    private async Task<string> SummarizeCoreAsync(IEnumerable<ReadOnlyMemory<byte>> history, GatewayHistorySource source, ModelProviderSettings snapshot,
        HttpListenerRequest incoming, GatewayIdentity identity, CancellationToken cancellation, ReadOnlyMemory<byte> rawInput, ReadOnlyMemory<byte> instructions)
    {
        if (!_stillSignedIn(identity)) throw new GatewayHistoryException("The original sign-in changed before the context handoff. No destination request was sent.");
        string endpoint, token;
        ProviderModel? model = null;
        if (source.Route == "openai")
        {
            if (snapshot.IsolatedProfile && !snapshot.DesktopProfile) throw new GatewayHistoryException("The source context belongs to native OpenAI. Return to its native profile to generate the handoff; the custom-only profile did not contact OpenAI.");
            endpoint = identity.ApiKey ? "https://api.openai.com/v1/responses" : "https://chatgpt.com/backend-api/codex/responses"; token = identity.Token;
        }
        else
        {
            var provider = snapshot.Providers.SingleOrDefault(item => item.Id == source.ProviderId);
            model = provider?.Models.SingleOrDefault(item => item.UpstreamId == source.Model);
            if (provider == null || model == null || Source(provider, model).Route != source.Route)
                throw new GatewayHistoryException("The source model or its original credential changed or was removed. Restore that provider to summarize its encrypted history. No other provider received it.");
            endpoint = new Uri(ProviderCatalog.Endpoint(provider.BaseUrl), "responses").AbsoluteUri; token = provider.ApiKey;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (source.Route == "openai" && identity.AccountId != null) request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", identity.AccountId);
        foreach (var header in new[] { "version", "User-Agent", "OpenAI-Beta" })
            if (incoming.Headers[header] is string value) request.Headers.TryAddWithoutValidation(header, value);
        var options = new JsonObject { ["model"] = source.Model, ["stream"] = true, ["store"] = false, ["instructions"] = ModelHistoryHandoff.SummaryInstructions,
            ["input"] = null, ["tools"] = new JsonArray(), ["tool_choice"] = "none", ["parallel_tool_calls"] = false };
        if (model != null) ModelRouting.ApplyReasoning(options, ProviderReasoning.Metadata(model, null));
        var values = history.ToList();
        if (!instructions.IsEmpty && !instructions.Span.SequenceEqual("null"u8))
        {
            if (instructions.Length > GatewayHistoryStore.MaximumSummaryBytes) throw new GatewayHistoryException("The original task instructions exceed the bounded handoff size. They were preserved; no source summary was requested.");
            var originalInstructions = JsonSerializer.Deserialize<string>(instructions.Span);
            if (!string.IsNullOrEmpty(originalInstructions))
                values.Insert(0, JsonSerializer.SerializeToUtf8Bytes(new { type = "message", role = "developer", content = new[] { new { type = "input_text", text = "Original task instructions to preserve in the context checkpoint:\n" + originalInstructions } } }));
        }
        request.Content = new GatewaySummaryContent(options, values.ToArray(), rawInput);
        using var result = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!result.IsSuccessStatusCode) throw new GatewayHistoryException("The source model could not generate a context handoff (HTTP " + (int)result.StatusCode + "). Its encrypted history was preserved and was not sent to the destination. Start a new explicit turn after restoring the source connection.");
        string summary;
        var mediaType = result.Content.Headers.ContentType?.MediaType;
        if (mediaType == null || string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await result.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = new StringBuilder(); var bytes = 0;
            while (await ReadLineAsync(reader, cancellation).ConfigureAwait(false) is string line)
            {
                bytes += Encoding.UTF8.GetByteCount(line);
                if (bytes > 4 * 1024 * 1024) throw new GatewayHistoryException("The source handoff stream exceeds its bound. The original history was preserved.");
                if (!line.StartsWith("data:", StringComparison.Ordinal) || line[5..].Trim() is "" or "[DONE]") continue;
                using var item = JsonDocument.Parse(line[5..].Trim()); var value = item.RootElement;
                var type = JsonValue.String(value, "type");
                if (type is "error" or "response.failed" or "response.incomplete") throw new GatewayHistoryException("The source handoff failed or was incomplete. The original history was preserved; no destination request was sent.");
                if (type == "response.output_item.done" && value.TryGetProperty("item", out var output) && JsonValue.String(output, "type") is not ("message" or "reasoning"))
                    throw new GatewayHistoryException("The source handoff attempted a non-message output. No tool was executed and no destination request was sent.");
                if (type == "response.output_text.delta") text.Append(JsonValue.String(value, "delta"));
                if (type == "response.completed")
                {
                    var response = value.GetProperty("response");
                    if (JsonValue.String(response, "status") != "completed") break;
                    var final = OutputText(response);
                    summary = final.Length == 0 ? text.ToString() : final;
                    ValidateSummary(summary);
                    if (!_stillSignedIn(identity)) throw new GatewayHistoryException("The sign-in changed during context handoff. No destination request was sent.");
                    return summary;
                }
            }
            throw new GatewayHistoryException("The source handoff ended before confirmed completion. No destination request was sent; a new explicit turn is required to retry.");
        }
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new GatewayHistoryException("The source handoff returned an unsupported response format. The original history was preserved; no destination request was sent.");
        var responseBytes = await ProviderCatalog.ReadLimitedAsync(result.Content, 4 * 1024 * 1024, cancellation).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(responseBytes))
        {
            if (JsonValue.String(document.RootElement, "status") != "completed") throw new GatewayHistoryException("The source handoff was not completed. Its original history was preserved.");
            summary = OutputText(document.RootElement);
        }
        ValidateSummary(summary);
        if (!_stillSignedIn(identity)) throw new GatewayHistoryException("The sign-in changed during context handoff. No destination request was sent.");
        return summary;
    }

    private static void ValidateSummary(string value)
    { if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > GatewayHistoryStore.MaximumSummaryBytes) throw new GatewayHistoryException("The source model did not return a usable bounded summary. The original history was preserved."); }
    private static string OutputText(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return "";
        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (JsonValue.String(item, "type") == "reasoning") continue;
            if (JsonValue.String(item, "type") != "message") throw new GatewayHistoryException("The source handoff included a tool or opaque output. No tool was executed and the original history was preserved.");
            if (item.TryGetProperty("content", out var content)) foreach (var part in content.EnumerateArray())
                if (JsonValue.String(part, "type") == "output_text") text.Append(JsonValue.String(part, "text"));
        }
        return text.ToString();
    }
    internal static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder(); var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false) != 0)
        {
            if (buffer[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= 1024 * 1024) throw new GatewayHistoryException("The source handoff event exceeds its bound.");
            text.Append(buffer[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }
    internal static GatewayHistorySource Source(ModelProvider provider, ProviderModel model) =>
        new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(provider.Id + "\n" + provider.BaseUrl + "\n" + provider.ApiKey))), model.UpstreamId, provider.Id);
    private static string RequestIdentity(string operation, GatewayHistoryInput input, int index)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(operation));
        foreach (var item in input.Items) hash.AppendData(item.Bytes.Span);
        hash.AppendData(BitConverter.GetBytes(index));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed class GatewaySummaryContent : HttpContent
{
    private readonly List<ReadOnlyMemory<byte>> _parts = new();
    internal GatewaySummaryContent(JsonObject options, ReadOnlyMemory<byte>[] history, ReadOnlyMemory<byte> rawInput)
    {
        options.Remove("input"); var bytes = JsonSerializer.SerializeToUtf8Bytes(options);
        _parts.Add(bytes.AsMemory(0, bytes.Length - 1)); _parts.Add(",\"input\":["u8.ToArray());
        for (var index = 0; index < history.Length; index++) { if (index != 0) _parts.Add(","u8.ToArray()); _parts.Add(history[index]); }
        if (!rawInput.IsEmpty)
        {
            if (history.Length != 0) _parts.Add(","u8.ToArray());
            _parts.Add("{\"role\":\"user\",\"content\":"u8.ToArray()); _parts.Add(rawInput); _parts.Add("}"u8.ToArray());
        }
        _parts.Add("]}"u8.ToArray()); Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }
    protected override bool TryComputeLength(out long length) { length = _parts.Sum(part => (long)part.Length); return true; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellation)
    {
        foreach (var part in _parts) await stream.WriteAsync(part, cancellation).ConfigureAwait(false);
    }
    protected override void Dispose(bool disposing) { if (disposing) _parts.Clear(); base.Dispose(disposing); }
}
