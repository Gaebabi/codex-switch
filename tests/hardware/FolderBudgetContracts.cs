using System;
using System.IO;
using System.Threading;
using CodexAccountDesk;

internal static class FolderBudgetContracts
{
    public static void Run(Action<string, Action> check)
    {
        static void Assert(bool value) { if (!value) throw new InvalidOperationException("Folder scan budget contract failed."); }
        check("Folder metadata queries never start faster than the global per-second limit", () =>
        {
            var time = TimeSpan.Zero;
            var budget = new FolderScanBudget(() => time, (delay, _) => time += delay);
            for (var index = 0; index <= FolderScanBudget.QueriesPerSecond; index++) budget.Query(() => 0, CancellationToken.None);
            Assert(time >= TimeSpan.FromSeconds(1) && budget.Queries == FolderScanBudget.QueriesPerSecond + 1);
        });
        check("A slow metadata query reserves four times its elapsed work as rest before another query", () =>
        {
            var time = TimeSpan.Zero;
            var budget = new FolderScanBudget(() => time, (delay, _) => time += delay);
            budget.Query(() => { time += TimeSpan.FromMilliseconds(50); return 0; }, CancellationToken.None);
            var started = TimeSpan.Zero;
            budget.Query(() => { started = time; return 0; }, CancellationToken.None);
            Assert(started >= TimeSpan.FromMilliseconds(250));
        });
        check("Cancellation during a throttle wait prevents the next filesystem query", () =>
        {
            var time = TimeSpan.Zero; using var stop = new CancellationTokenSource();
            var budget = new FolderScanBudget(() => time, (_, _) => stop.Cancel());
            budget.Query(() => 0, stop.Token); var canceled = false;
            try { budget.Query<int>(() => throw new InvalidOperationException("A canceled query was executed."), stop.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled && budget.Queries == 1);
        });
        check("Failed native queries consume their work budget instead of creating an immediate retry burst", () =>
        {
            var time = TimeSpan.Zero;
            var budget = new FolderScanBudget(() => time, (delay, _) => time += delay);
            try { budget.Query<int>(() => { time += TimeSpan.FromMilliseconds(50); throw new IOException("Fixture failure"); }, CancellationToken.None); }
            catch (IOException) { }
            budget.Query(() => 0, CancellationToken.None);
            Assert(time >= TimeSpan.FromMilliseconds(250) && budget.Queries == 2);
        });
    }
}
