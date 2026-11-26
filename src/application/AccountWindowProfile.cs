using System;
using System.IO;
using System.Text;

namespace CodexAccountDesk;

internal sealed class AccountWindowProfile(string directory, string key)
{
    private readonly string _root = Path.Combine(directory, "account-windows");
    private string Folder => Path.Combine(_root, SecureFiles.Hash(key));
    internal string Home => Path.Combine(Folder, "home");
    internal string Desktop => Path.Combine(Folder, "desktop");
    private string Auth => Path.Combine(Home, "auth.json");

    private static void CheckPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Account window storage contains a linked path.");
    }

    internal string ReadLogin()
    {
        CheckPath(_root); CheckPath(Folder); CheckPath(Desktop); CheckPath(Home);
        string json;
        try { CheckPath(Auth); json = File.ReadAllText(Auth); }
        catch (FileNotFoundException) { throw new AccountLoginRequiredException(); }
        if (AuthDocument.Parse(json).Key != key) throw new InvalidOperationException("switch.window_identity");
        return json;
    }

    internal void Prepare(AccountProfile account)
    {
        if (AuthDocument.Parse(account.AuthJson).Key != key) throw new InvalidOperationException("switch.window_identity");
        foreach (var path in new[] { _root, Folder, Home, Desktop }) SecureFiles.CreatePrivateDirectory(path);
        var config = Path.Combine(Home, "config.toml");
        if (!account.HasAccountWindow && !File.Exists(config)) SecureFiles.AtomicWriteText(config, "cli_auth_credentials_store = \"file\"\n");
        var hasLogin = File.Exists(Auth);
        if (hasLogin) _ = ReadLogin();
        else if (account.HasAccountWindow && !account.WindowLoginPending) throw new AccountLoginRequiredException();
        if (!account.HasAccountWindow || account.WindowLoginPending)
        {
            if (!hasLogin || account.WindowLoginPending) SecureFiles.AtomicWriteOwned(Auth, Encoding.UTF8.GetBytes(account.AuthJson));
            account.HasAccountWindow = true;
            account.WindowLoginPending = false;
        }
    }

    internal void RemoveLogin()
    {
        if (File.Exists(Auth)) { _ = ReadLogin(); File.Delete(Auth); }
    }
}
