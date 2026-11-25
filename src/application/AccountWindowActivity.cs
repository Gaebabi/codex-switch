using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class AccountWindowActivity
{
    private readonly object _gate = new();
    private int _running;
    private long _accountGeneration;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal (int Running, long AccountGeneration, Task Changed) Snapshot()
    {
        lock (_gate) return (_running, _accountGeneration, _changed.Task);
    }

    internal void Update(int count, bool accountChanged = false)
    {
        lock (_gate)
        {
            if (!accountChanged && count == _running) return;
            _running = count;
            if (accountChanged) _accountGeneration++;
            var prior = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            prior.TrySetResult();
        }
    }
}
