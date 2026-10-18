using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace CodexMeter.Windows
{
    internal static class Program
    {
        private const string InstanceName = "Local\\CodexMeter.Windows.Basic.v1";
        [STAThread]
        private static int Main(string[] args)
        {
            string fixtureToken = null;
            try
            {
                if (args.Length == 3 && args[0] == "--install-fixture") { UpdateFixture.Initialize(args[1]); return UpdateFixture.Install(args[2]); }
                if (args.Length == 3 && args[0] == "--apply-update-fixture") { UpdateFixture.Initialize(args[2]); return UpdateWorker.Apply(args[1]); }
                if (args.Length == 2 && args[0] == "--rollback-fixture") { UpdateFixture.Initialize(args[1]); return UpdateFixture.Rollback(); }
                if (args.Length == 3 && args[0] == "--update-token-fixture")
                {
                    UpdateFixture.Initialize(args[2]); fixtureToken = args[1];
                    if (System.IO.File.ReadAllText(System.IO.Path.Combine(UpdateFixture.DirectoryPath, "startup-mode")) == "fail") return 74;
                    args = new[] { "--preview" };
                }
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; return 2; }
            if (args.Length == 2 && args[0] == "--apply-update") return UpdateWorker.Apply(args[1]);
            var preview = args.Contains("--preview") || args.Contains("--smoke");
            bool created;
            string instance = UpdateFixture.Enabled ? InstanceName + ".Integration." + System.IO.Path.GetFileName(UpdateFixture.DirectoryPath) : preview ? InstanceName + ".Preview" : InstanceName;
            using (var singleton = new Mutex(true, instance, out created))
            {
                if (!created)
                {
                    if (!preview) PostMessage(new IntPtr(0xffff), RegisterWindowMessage("CodexMeter.Windows.Show"), IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (var controller = new TrayApplication(app, args))
                    {
                        controller.Start();
                        if (fixtureToken != null)
                        { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => UpdateWorker.Ready(fixtureToken))); }
                        if (!preview && args.Length == 2 && args[0] == "--update-token")
                        { app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => UpdateWorker.Ready(args[1]))); }
                        app.Run();
                    }
                    return 0;
                }
                catch (Exception)
                {
                    MessageBox.Show("CodexMeter 无法启动。请确认解压了完整文件夹，并检查本地设置目录是否可访问。",
                        "CodexMeter", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { singleton.ReleaseMutex(); }
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
