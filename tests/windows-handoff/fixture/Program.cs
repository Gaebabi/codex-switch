using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        var delay = int.Parse(args[2]);
        if (delay is < 0 or > 35000) throw new ArgumentOutOfRangeException(nameof(delay));
        if (args[0] == "sentinel")
        {
            var survivor = new ShutdownForm();
            survivor.Load += (_, _) => { File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString()); File.Move(args[1] + ".tmp", args[1]); };
            Application.Run(survivor); return;
        }
        if (args[0] == "headless")
        {
            File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString()); File.Move(args[1] + ".tmp", args[1]);
            Thread.Sleep(35000); return;
        }
        if (args[0] == "child")
        {
            using var parent = Process.GetProcessById(int.Parse(args[3]));
            File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString()); File.Move(args[1] + ".tmp", args[1]);
            parent.WaitForExit(); Thread.Sleep(delay); return;
        }
        var form = new ShutdownForm();
        form.Load += (_, _) =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "child", args[1], args[2], Environment.ProcessId.ToString() }) start.ArgumentList.Add(value);
            using var child = Process.Start(start) ?? throw new IOException("Fixture child did not start");
        };
        Application.Run(form);
    }
    private sealed class ShutdownForm : Form
    {
        public ShutdownForm()
        { Text = "Disposable handoff test"; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Left = -12000; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x11)
            { message.Result = new IntPtr(1); return; }
            if (message.Msg == 0x16 && message.WParam != IntPtr.Zero) { Close(); return; }
            base.WndProc(ref message);
        }
    }
}
