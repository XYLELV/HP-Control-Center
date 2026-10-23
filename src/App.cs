using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace HPControlPanel
{
    public static class App
    {
        // Off-screen layout check: no network (weather) or other side effects.
        public static bool SnapshotMode;

        public static void Log(Exception e)
        {
            try { File.AppendAllText(Path.Combine(Settings.DataDir, "error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e + Environment.NewLine + Environment.NewLine); }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        // Launched again while already open (shortcut, Start search): bring the open window to the front.
        static void ActivateRunningInstance()
        {
            var me = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
            {
                using (p)
                {
                    if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9 /*SW_RESTORE*/);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            // --snapshot <png> [NavName] [lang]: layout check used during development (no single-instance lock).
            // --restart [NavName]: started by the app itself after a language switch; waits for the old window to close.
            bool snapshot = args.Length >= 2 && args[0] == "--snapshot";
            bool restart = args.Length >= 1 && args[0] == "--restart";
            SnapshotMode = snapshot;
            L.Lang = L.Pick(snapshot && args.Length > 3 ? args[3] : Settings.Load().Language);
            bool first;
            using (var single = new Mutex(true, snapshot ? @"Local\HPControlPanelSnapshot" : @"Local\HPControlPanel", out first))
            {
                if (!first && restart)
                {
                    try { first = single.WaitOne(60000); }   // generous: the old window may be finishing a slow WMI / EC read
                    catch (AbandonedMutexException) { first = true; }   // the old instance exited without releasing it
                }
                if (!first) { ActivateRunningInstance(); return 0; }
                string title = L.S("HP 控制台", "HP Control Center");
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (o, e) =>
                {
                    Log(e.Exception);
                    MessageBox.Show(L.S("出现了一个错误，详情已写入 error.log：\n", "Something went wrong (details in error.log):\n") + e.Exception.Message, title);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log(e.ExceptionObject as Exception ?? new Exception("unknown"));
                try
                {
                    var w = new MainWindow();
                    if (snapshot) w.SnapshotAndExit(args[1], args.Length > 2 ? args[2] : null);
                    else ThreadPool.QueueUserWorkItem(_ => { Settings.EnsureStartMenuShortcut(); Settings.RepairAutoStart(); });
                    if (restart && args.Length > 1) w.ShowPage(args[1]);
                    return app.Run(w.W);
                }
                catch (Exception e)
                {
                    Log(e);
                    MessageBox.Show(L.S("启动失败，详情已写入 error.log：\n", "Couldn't start (details in error.log):\n") + e.Message, title);
                    return 1;
                }
            }
        }
    }
}
