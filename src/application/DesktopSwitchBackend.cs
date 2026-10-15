using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class DesktopSwitchBackend : IAccountSwitchBackend
{
    private readonly AccountService _service;
    private readonly DesktopSessionMonitor _sessions;
    private CodexRpc? _metadata;
    private LinuxClientSession? _linuxMetadata;
    public DesktopSwitchBackend(AccountService service, DesktopSessionMonitor sessions) { _service = service; _sessions = sessions; }
    public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => OperatingSystem.IsLinux() ? _service.CurrentAccountKeyAsync(cancellation) : Task.Run(() =>
        File.Exists(_service.AuthPath) ? AuthDocument.Parse(File.ReadAllText(_service.AuthPath)).Key : null, cancellation);
    public async Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation)
    {
        if (OperatingSystem.IsLinux())
        {
            if (_service.LinuxDesktop == null)
            {
                if (LinuxDesktopRuntime.Running(cancellation).Length != 0) throw new LinuxDesktopOwnershipException();
                return Array.Empty<SwitchChat>();
            }
            if (!_service.LinuxDesktop.RequireOwnedOrStopped(cancellation)) return Array.Empty<SwitchChat>();
        }
        else
        {
            if (!File.Exists(_service.Installation.DesktopPath)) throw new FileNotFoundException("Codex 데스크톱 앱을 찾을 수 없습니다.");
            if (!RuntimeLocator.IsDesktopRunning(_service.Installation.DesktopPath)) return Array.Empty<SwitchChat>();
        }
        await _sessions.WaitForDiscoveryAsync(cancellation);
        var chats = _sessions.Threads.Where(thread => thread.Status == "active").Select(thread => new SwitchChat {
            Id = thread.Id, OriginalTitle = thread.Title ?? "", OriginalTurnId = thread.LastTurnId, OriginalGoal = thread.Goal }).ToArray();
        foreach (var chat in chats.Where(chat => OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(chat.OriginalTitle)))
        {
            var result = await RequestMetadataAsync("thread/read", new { threadId = chat.Id, includeTurns = false }, cancellation);
            await _service.CheckThreadContextAsync(result.GetProperty("thread"), _service.CurrentKey, cancellation);
            chat.OriginalTitle = JsonValue.String(result.GetProperty("thread"), "name") ?? "";
        }
        return chats;
    }
    private async Task<CodexRpc> MetadataAsync(CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Desktop metadata actions require a verified desktop lifecycle and peer provider.");
        if (_metadata == null)
        {
            _metadata = new CodexRpc(_service.Installation.CliPath, _service.CodexHome, true);
            await _metadata.InitializeAsync(cancellation);
        }
        return _metadata;
    }
    private async Task<JsonElement> RequestMetadataAsync(string method, object parameters, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) return await (await MetadataAsync(cancellation)).RequestAsync(method, parameters, cancellation);
        _linuxMetadata ??= await _service.OpenMetadataSessionAsync(cancellation);
        return await _linuxMetadata.MetadataAsync(method, parameters, cancellation);
    }
    public async Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation)
    {
        if (OperatingSystem.IsLinux() && (_service.LinuxDesktop == null || !_service.LinuxDesktop.RequireOwnedOrStopped(cancellation)))
            throw new LinuxDesktopOwnershipException();
        var before = await RequestMetadataAsync("thread/read", new { threadId = chat.Id, includeTurns = false }, cancellation);
        await _service.CheckThreadContextAsync(before.GetProperty("thread"), _service.CurrentKey, cancellation);
        chat.OriginalTitle = JsonValue.String(before.GetProperty("thread"), "name") ?? chat.OriginalTitle;
        chat.BackupTitle = SwitchTitle.Stamp(chat.OriginalTitle, at);
        await RequestMetadataAsync("thread/name/set", new { threadId = chat.Id, name = chat.BackupTitle }, cancellation);
        var result = await RequestMetadataAsync("thread/read", new { threadId = chat.Id, includeTurns = false }, cancellation);
        if (JsonValue.String(result.GetProperty("thread"), "name") != chat.BackupTitle)
            throw new InvalidDataException("채팅 이름 저장을 확인하지 못했습니다.");
    }
    public async Task CloseDesktopAsync(CancellationToken cancellation)
    {
        await DisposeAsync();
        if (OperatingSystem.IsLinux()) { if (_service.LinuxDesktop != null) await _service.LinuxDesktop.CloseAsync(cancellation); }
        else await DesktopShutdown.CloseAsync(_service.Installation.DesktopPath, cancellation);
        _sessions.RestartDiscovery();
    }
    public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation) => _service.ApplySwitchAsync(key, null, false, cancellation, source, true);
    public async Task LaunchDesktopAsync(CancellationToken cancellation)
    {
        if (OperatingSystem.IsLinux())
        {
            if (_service.LinuxDesktop == null) return;
            await _service.LinuxDesktop.StartAsync(cancellation);
        }
        else if (!RuntimeLocator.IsDesktopRunning(_service.Installation.DesktopPath)) RuntimeLocator.LaunchDesktop(_service.Installation.DesktopPath);
        await _sessions.WaitForDiscoveryAsync(cancellation);
    }
    public async Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation)
    {
        if (OperatingSystem.IsLinux())
        {
            var metadata = await RequestMetadataAsync("thread/read", new { threadId = chat.Id, includeTurns = false }, cancellation);
            await _service.CheckThreadContextAsync(metadata.GetProperty("thread"), _service.CurrentKey, cancellation);
            await _service.LinuxDesktop!.OpenThreadAsync(chat.Id, cancellation);
        }
        else RuntimeLocator.OpenDesktopThread(_service.Installation.DesktopPath, chat.Id);
        var thread = await _sessions.WaitForThreadAsync(chat.Id, cancellation);
        if (SwitchContinuation.Decide(chat, thread) is ChatResumeState decided) return decided;
        if (thread.Goal == "active")
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(150, cancellation);
                thread = _sessions.Threads.FirstOrDefault(item => item.Id == chat.Id) ?? await _sessions.WaitForThreadAsync(chat.Id, cancellation);
                if (thread.Status == "active") return ChatResumeState.AlreadyRunning;
            }
        }
        thread = await _sessions.WaitForThreadAsync(chat.Id, cancellation);
        if (SwitchContinuation.Decide(chat, thread) is ChatResumeState settled) return settled;
        sending();
        var result = await _sessions.SendToOwnerAsync(thread, "thread-follower-start-turn", SwitchContinuation.StartParameters(chat), 2, cancellation);
        var turn = result.GetProperty("result").GetProperty("turn");
        if (string.IsNullOrEmpty(JsonValue.String(turn, "id"))) throw new InvalidDataException("채팅 재개 응답을 확인하지 못했습니다.");
        return ChatResumeState.Resumed;
    }
    public async ValueTask DisposeAsync()
    {
        var rpc = _metadata; _metadata = null;
        if (rpc != null) await rpc.DisposeAsync();
        var linux = _linuxMetadata; _linuxMetadata = null;
        if (linux != null) await linux.DisposeAsync();
    }
}

internal static class SwitchContinuation
{
    public static object StartParameters(SwitchChat chat) => new { conversationId = chat.Id,
        turnStart = new { request = new { threadId = chat.Id, input = new[] { new { type = "text", text = "Continue", text_elements = Array.Empty<object>() } },
            clientUserMessageId = chat.ContinueMessageId }, context = new { inheritThreadSettings = true } } };
    public static ChatResumeState? Decide(SwitchChat chat, DesktopThreadState thread)
    {
        if (thread.Status == "active") return ChatResumeState.AlreadyRunning;
        if (thread.Status != "idle") return ChatResumeState.Unconfirmed;
        if (chat.ResumeState is ChatResumeState.Sending or ChatResumeState.Unconfirmed)
            return thread.LastMessageId == chat.ContinueMessageId && thread.LastTurnId != chat.OriginalTurnId && thread.LastTurnStatus == "completed" ?
                ChatResumeState.Completed : ChatResumeState.Unconfirmed;
        if (thread.LastTurnId != null && chat.OriginalTurnId != null && thread.LastTurnId != chat.OriginalTurnId) return ChatResumeState.Completed;
        if (thread.LastTurnId == chat.OriginalTurnId && thread.LastTurnId != null && thread.LastTurnStatus == "completed" && thread.Goal != "active") return ChatResumeState.Completed;
        return null;
    }
}
