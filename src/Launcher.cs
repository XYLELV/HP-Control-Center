// Quick-launch entries: open (un-elevated, via the shell) or close a program.
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;

namespace HPControlPanel
{
    public enum CloseKind { Graceful, Kill, ExplorerWindows }

    public class AppEntry
    {
        public string Name, Icon, Target;
        public string[] ProcessNames;
        public CloseKind Close = CloseKind.Graceful;
        public bool Custom;
    }

    public static class Launcher
    {
        static readonly string Sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        public static List<AppEntry> BuiltIn()
        {
            return new List<AppEntry>
            {
                new AppEntry { Name = L.S("写字板", "WordPad"), Icon = "📝", Target = Path.Combine(Sys32, "write.exe"), ProcessNames = new[] { "wordpad" } },
                new AppEntry { Name = L.S("记事本", "Notepad"), Icon = "🗒", Target = Path.Combine(Sys32, "notepad.exe"), ProcessNames = new[] { "notepad" } },
                new AppEntry { Name = L.S("计算器", "Calculator"), Icon = "🧮", Target = Path.Combine(Sys32, "calc.exe"), ProcessNames = new[] { "Calculator", "CalculatorApp" }, Close = CloseKind.Kill },
                new AppEntry { Name = L.S("文件管理", "File Explorer"), Icon = "📁", Target = "", ProcessNames = new string[0], Close = CloseKind.ExplorerWindows },
                new AppEntry { Name = L.S("设置", "Settings"), Icon = "⚙", Target = "ms-settings:", ProcessNames = new[] { "SystemSettings" }, Close = CloseKind.Kill },
            };
        }

        public static AppEntry FromPath(string path)
        {
            // For a shortcut, the running process is named after the shortcut's target, not the .lnk.
            string exe = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    string t = sh.CreateShortcut(path).TargetPath;
                    if (!string.IsNullOrEmpty(t)) exe = t;
                }
                catch { }
            }
            return new AppEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Icon = "⭐",
                Target = path,
                ProcessNames = new[] { Path.GetFileNameWithoutExtension(exe) },
                Custom = true
            };
        }

        public static bool IsRunning(AppEntry a)
        {
            if (a.Close == CloseKind.ExplorerWindows) return ExplorerWindows().Any();
            foreach (var n in a.ProcessNames)
            {
                var ps = Process.GetProcessesByName(n);
                bool any = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
                if (any) return true;
            }
            return false;
        }

        // This app runs as administrator; starting through explorer.exe keeps the launched program un-elevated.
        public static void Open(AppEntry a)
        {
            string arg = a.Close == CloseKind.ExplorerWindows ? "" : "\"" + a.Target + "\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arg) { UseShellExecute = false });
        }

        public static void CloseApp(AppEntry a)
        {
            if (a.Close == CloseKind.ExplorerWindows)
            {
                foreach (var w in ExplorerWindows()) { try { w.Quit(); } catch { } }
                return;
            }
            foreach (var n in a.ProcessNames)
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try
                    {
                        // Graceful close lets WordPad/Notepad ask to save unsaved work.
                        if (a.Close == CloseKind.Kill) p.Kill();
                        else p.CloseMainWindow();
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
        }

        static IEnumerable<dynamic> ExplorerWindows()
        {
            var result = new List<dynamic>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        string full = w.FullName;
                        if (full != null && full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) result.Add(w);
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }
    }
}
