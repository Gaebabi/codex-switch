using System;

namespace CodexAccountDesk;

internal interface IResourceMonitor : IAsyncDisposable
{
    HardwareUsage Current { get; }
    event Action? Changed;
    void Start();
}
