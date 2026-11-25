using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Pipes;
using CodexAccountDesk;

internal static class AccountWindowIdentityContracts
{
    internal static async Task RunAsync(Action<string, bool> check)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "fixture-sub", ["https://api.openai.com/auth"] = new { chatgpt_user_id = "fixture-user" } };
        var idToken = "e30." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims)).TrimEnd('=').Replace('+','-').Replace('/','_') + ".fixture";
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new {
            account_id = "fixture-workspace", id_token = idToken, access_token = "fictional", refresh_token = "fictional" } });
        var account = AuthDocument.Parse(auth);
        var session = new JsonObject { ["isActive"] = true, ["sessionId"] = "s1", ["selectedWorkspaceAccountId"] = account.AccountId, ["userId"] = "fixture-user" };
        var report = new JsonObject { ["sessions"] = new JsonArray(session), ["activeSessionId"] = "s1" };
        check("A native session for the selected user and workspace confirms the window", AccountWindowIdentity.VerifiedKey(auth, report) == account.Key);
        session["userId"] = "different";
        check("A different native user cannot confirm an account window", AccountWindowIdentity.VerifiedKey(auth, report) == "");
        session["userId"] = "fixture-user"; session["selectedWorkspaceAccountId"] = "different";
        check("A different native workspace cannot confirm an account window", AccountWindowIdentity.VerifiedKey(auth, report) == "");
        session["selectedWorkspaceAccountId"] = account.AccountId; report["activeSessionId"] = "other";
        check("An inactive native session cannot confirm an account window", AccountWindowIdentity.VerifiedKey(auth, report) == "");
        if (!OperatingSystem.IsWindows()) return;
        report["activeSessionId"] = "s1";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "window-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "auth.json"), auth);
            var state = new AccountWindowActivity();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var server = AccountWindowIdentity.ServeAsync(folder, (_, _) => Task.FromResult<JsonNode?>(report), state, deadline.Token);
            try
            {
                using var client = new NamedPipeClientStream(".", AccountWindowIdentity.Pipe(folder), PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(deadline.Token);
                await client.WriteAsync(new byte[] { 2 }, deadline.Token);
                var identity = new byte[65];
                await client.ReadExactlyAsync(identity, deadline.Token);
                check("Subscription starts with the verified account", Encoding.ASCII.GetString(identity) == account.Key + "\n");
                var frame = new byte[sizeof(int)];
                foreach (var count in new[] { 0, 2, 1, 0 })
                {
                    state.Update(count);
                    await client.ReadExactlyAsync(frame, deadline.Token);
                    check($"Live account window shows {count} running chats", BitConverter.ToInt32(frame) == count);
                }
                state.Update(0, accountChanged: true);
                check("A login change disconnects the previous account subscriber", await client.ReadAsync(frame, deadline.Token) == 0);
            }
            finally { deadline.Cancel(); await server; }
        }
        finally { File.Delete(Path.Combine(folder, "auth.json")); Directory.Delete(folder); }
    }
}
