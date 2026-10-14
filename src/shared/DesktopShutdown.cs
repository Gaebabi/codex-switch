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
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern uint RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern uint RmRegisterResources(uint session, uint files, string[]? names,
        uint applications, UniqueProcess[] processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern uint RmShutdown(uint session, uint flags, IntPtr progress);
    [DllImport("rstrtmgr.dll")] private static extern uint RmCancelCurrentTask(uint session);
    [DllImport("rstrtmgr.dll")] private static extern uint RmEndSession(uint session);
    public static Task CloseAsync(string executable, CancellationToken cancellation) => OperatingSystem.IsMacOS()
        ? MacDesktop.CloseAsync(executable, cancellation) : CloseWindowsAsync(executable, cancellation);
    private static Task CloseWindowsAsync(string executable, CancellationToken cancellation) => Task.Run(() =>
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
            using var cancel = cancellation.Register(() => RmCancelCurrentTask(session));
            var result = RmShutdown(session, 0, IntPtr.Zero);
            cancellation.ThrowIfCancellationRequested();
            if (result != 0) throw new IOException("Codex를 정상 종료하지 못했습니다. Codex 메뉴에서 종료한 뒤 다시 전환해 주세요.", new Win32Exception((int)result));
            if (RuntimeLocator.IsDesktopRunning(executable)) throw new IOException("Codex가 아직 실행 중입니다. Codex 메뉴에서 완전히 종료해 주세요.");
        }
        finally { RmEndSession(session); }
    }, cancellation);
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
