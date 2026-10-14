using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class QueueDiagnostics
{
    public static async Task<int> VerifyAsync(string home, string reportPath)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        DesktopQueueSnapshot? snapshot = null;
        string? error = null;
        try { snapshot = await DesktopQueueState.ReadAsync(home, CancellationToken.None); }
        catch (InvalidDataException e) { error = e.Message; }
        catch (IOException e) { error = e.Message; }
        catch (JsonException) { error = "Invalid saved queue JSON."; }
        catch (InvalidOperationException) { error = "Invalid queue metadata."; }
        catch (UnauthorizedAccessException) { error = "Queue metadata access denied."; }
        var total = snapshot?.Saved.Keys.Concat(snapshot.Server.Keys).Distinct().Sum(id => DesktopQueueState.Count(
            snapshot.Saved.GetValueOrDefault(id) ?? Array.Empty<DesktopQueueMessage>(),
            snapshot.Server.GetValueOrDefault(id) ?? Array.Empty<DesktopQueueMessage>()).Total);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = "1.22.0", available = snapshot != null, error,
            saved = snapshot?.Saved.Values.Sum(m => m.Length), server = snapshot?.Server.Values.Sum(m => m.Length), total,
            elapsedMs = started.Elapsed.TotalMilliseconds, scope = "Read-only saved/server queue identities, no message text, account access or task mutation." },
            new JsonSerializerOptions { WriteIndented = true }));
        return snapshot != null ? 0 : 1;
    }
}
