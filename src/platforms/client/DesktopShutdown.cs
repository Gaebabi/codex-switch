using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class DesktopShutdown
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { public uint Id; public System.Runtime.InteropServices.ComTypes.FILETIME Started; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string File;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestartProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public int Type; public uint Status, Session; public int Restartable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern uint RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern uint RmRegisterResources(uint session, uint files, string[]? names,
        uint applications, UniqueProcess[] processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern uint RmShutdown(uint session, uint flags, IntPtr progress);
    [DllImport("rstrtmgr.dll")] private static extern uint RmGetList(uint session, out uint needed, ref uint count,
        [In, Out] RestartProcessInfo[]? affected, out uint rebootReasons);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern uint RmAddFilter(uint session, string? module,
        ref UniqueProcess process, string? service, int action);
    [DllImport("rstrtmgr.dll")] private static extern uint RmCancelCurrentTask(uint session);
    [DllImport("rstrtmgr.dll")] private static extern uint RmEndSession(uint session);
    public static Task CloseAsync(string executable, CancellationToken cancellation)
    {
        if (OperatingSystem.IsMacOS()) return MacDesktop.CloseAsync(executable, cancellation);
        if (OperatingSystem.IsWindows()) return CloseWindowsAsync(executable, cancellation);
        throw new PlatformNotSupportedException("Codex normal shutdown requires a supported platform provider.");
    }
    private static Task CloseWindowsAsync(string executable, CancellationToken cancellation) => Task.Run(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        var processes = new List<UniqueProcess>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    var started = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                    processes.Add(new() { Id = (uint)process.Id, Started = new() { dwLowDateTime = (int)started, dwHighDateTime = (int)(started >> 32) } });
                }
                catch (InvalidOperationException) { }
            }
        }
        if (processes.Count == 0) return;
        var parents = ReadParents(); var ids = new HashSet<uint>(processes.ConvertAll(process => process.Id));
        processes.RemoveAll(process => parents.TryGetValue(process.Id, out var parent) && ids.Contains(parent));
        if (processes.Count == 0) throw new IOException("Codex의 기본 프로세스를 확인하지 못했습니다.");
        Check(RmStartSession(out var session, 0, new StringBuilder(33)));
        try
        {
            Check(RmRegisterResources(session, 0, null, (uint)processes.Count, processes.ToArray(), 0, null));
            var affected = AffectedProcesses(session);
            ValidateAffected(processes, affected);
            // RmShutdown refreshes its own affected list, so protect this controller
            // by exact PID/start-time even if the list changes after our inspection.
            using var self = Process.GetCurrentProcess();
            var selfStart = self.StartTime.ToUniversalTime().ToFileTimeUtc();
            var selfIdentity = new UniqueProcess { Id = (uint)self.Id,
                Started = new() { dwLowDateTime = (int)selfStart, dwHighDateTime = (int)(selfStart >> 32) } };
            Check(RmAddFilter(session, null, ref selfIdentity, null, 2)); // RmNoShutdown
            using var cancel = cancellation.Register(() => RmCancelCurrentTask(session));
            var result = RmShutdown(session, 0, IntPtr.Zero);
            cancellation.ThrowIfCancellationRequested();
            if (result != 0) throw new IOException("Codex를 정상 종료하지 못했습니다. Codex 메뉴에서 종료한 뒤 다시 전환해 주세요.", new Win32Exception((int)result));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { while (RuntimeLocator.IsDesktopRunning(executable)) await Task.Delay(100, timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new IOException("Codex가 아직 실행 중입니다. Codex 메뉴에서 완전히 종료해 주세요."); }
        }
        finally { RmEndSession(session); }
    }, cancellation);
    private static bool SameProcess(UniqueProcess left, UniqueProcess right) => left.Id == right.Id &&
        left.Started.dwLowDateTime == right.Started.dwLowDateTime && left.Started.dwHighDateTime == right.Started.dwHighDateTime;
    private static void ValidateAffected(List<UniqueProcess> roots, RestartProcessInfo[] affected)
    {
        if (affected.Length == 0 || Array.Exists(affected, item => !roots.Exists(root => SameProcess(root, item.Process))))
            throw new IOException("Codex 종료 대상에 다른 프로그램이 포함되어 계정 전환을 중단했습니다.");
    }
    internal static void ValidateAffectedForTest((int Pid, long Start)[] roots, (int Pid, long Start)[] affected)
    {
        static UniqueProcess Convert((int Pid, long Start) item) => new() { Id = (uint)item.Pid,
            Started = new() { dwLowDateTime = (int)item.Start, dwHighDateTime = (int)(item.Start >> 32) } };
        var list = new List<UniqueProcess>(Array.ConvertAll(roots, Convert));
        ValidateAffected(list, Array.ConvertAll(affected, item => new RestartProcessInfo { Process = Convert(item) }));
    }
    private static RestartProcessInfo[] AffectedProcesses(uint session)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            uint count = 0;
            var result = RmGetList(session, out var needed, ref count, null, out var reboot);
            if (result == 0 && needed == 0) return Array.Empty<RestartProcessInfo>();
            if (result != 234) { Check(result); throw new IOException("Codex 종료 대상을 확인할 수 없습니다."); }
            if (needed > 1024 || reboot != 0) throw new IOException("Codex 종료 대상을 안전하게 확인하지 못했습니다.");
            var items = new RestartProcessInfo[needed]; count = needed;
            result = RmGetList(session, out _, ref count, items, out reboot);
            if (result == 234) continue;
            Check(result);
            if (reboot != 0) throw new IOException("Codex 종료에 시스템 재시작이 필요합니다.");
            Array.Resize(ref items, (int)count); return items;
        }
        throw new IOException("Codex 종료 대상이 계속 변경되어 전환을 중단했습니다.");
    }
    private static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
    private static Dictionary<uint, uint> ReadParents()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), File = "" };
            if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var parents = new Dictionary<uint, uint>();
            do { parents[entry.Id] = entry.Parent; } while (Process32NextW(snapshot, ref entry));
            var error = Marshal.GetLastWin32Error(); if (error != 18) throw new Win32Exception(error);
            return parents;
        }
        finally { CloseHandle(snapshot); }
    }
}
