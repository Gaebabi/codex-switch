using System;
using System.IO;
using System.Threading;

namespace CodexAccountDesk;

internal sealed class VaultTransaction : IDisposable
{
    private readonly Mutex _mutex;
    internal VaultTransaction(string path)
    {
        var identity = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        _mutex = new Mutex(false, "CodexSwitch.Vault." + SecureFiles.Hash(identity));
        try
        {
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(30))) throw new IOException("The protected store is busy. No data was changed.");
        }
        catch (AbandonedMutexException) { }
        catch { _mutex.Dispose(); throw; }
    }
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
