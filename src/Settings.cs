// settings.ini in %APPDATA%\HPControlPanel: simple key=value lines.
// The exe itself lives in Program Files (admin-only), so nothing the elevated app executes or loads is user-writable.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace HPControlPanel
{
    public class Settings
    {
        public string Theme = "blue";        // blue | pink | purple
        public int RefreshSec = 1;           // 1 | 2 | 5
        public int AlertTemp = 85;
        public bool TopMost;
        public string MascotImage = "";
        public List<string> CustomApps = new List<string>();
        public string WeatherMode = "location";   // location | ip | manual
        public string WeatherPlace = "";          // manual mode: chosen city name
        public double WeatherLat, WeatherLon;
        public bool AutoUpdate = true;            // check GitHub at most once a day
        public long LastUpdateCheck;              // DateTime ticks (UTC)
        public string AnnouncedVersion = "";      // newest version HP 娘 already mentioned
        public string Language = "";              // zh-CN | zh-TW | en; empty = follow Windows

        // Program folder (exe + LpcACPIEC.bin), read-only for the app.
        public static string Dir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        // Per-user data: settings.ini, custom-mascot.*, error.log.
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HPControlPanel");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        static string FilePath { get { return Path.Combine(DataDir, "settings.ini"); } }
        // Where versions up to 2026-10-22 kept the exe and settings.
        static string LegacyDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HPControlPanel"); } }

        // One-time move of settings.ini and custom-mascot.* from the old install folder.
        static void MigrateLegacy()
        {
            try
            {
                if (File.Exists(FilePath) || !File.Exists(Path.Combine(LegacyDir, "settings.ini"))) return;
                foreach (var f in Directory.GetFiles(LegacyDir, "custom-mascot.*"))
                    File.Copy(f, Path.Combine(DataDir, Path.GetFileName(f)), true);
                var lines = File.ReadAllLines(Path.Combine(LegacyDir, "settings.ini"), Encoding.UTF8)
                    .Select(l => l.StartsWith("MascotImage=") ? l.Replace(LegacyDir, DataDir) : l);
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception e) { App.Log(e); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                MigrateLegacy();
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "Theme": s.Theme = v; break;
                        case "RefreshSec": if (int.TryParse(v, out n)) s.RefreshSec = Math.Max(1, Math.Min(10, n)); break;
                        case "AlertTemp": if (int.TryParse(v, out n)) s.AlertTemp = Math.Max(60, Math.Min(95, n)); break;   // same range as AlertSlider
                        case "TopMost": s.TopMost = v == "1"; break;
                        case "MascotImage": s.MascotImage = v; break;
                        case "CustomApp": if (v.Length > 0) s.CustomApps.Add(v); break;
                        case "WeatherMode": if (v == "location" || v == "ip" || v == "manual") s.WeatherMode = v; break;
                        case "WeatherPlace": s.WeatherPlace = v; break;
                        case "WeatherLat": double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.WeatherLat); break;
                        case "WeatherLon": double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.WeatherLon); break;
                        case "AutoUpdate": s.AutoUpdate = v != "0"; break;
                        case "LastUpdateCheck": long.TryParse(v, out s.LastUpdateCheck); break;
                        case "AnnouncedVersion": s.AnnouncedVersion = v; break;
                        case "Language": if (v == "zh-CN" || v == "zh-TW" || v == "en") s.Language = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Theme=" + Theme);
            sb.AppendLine("RefreshSec=" + RefreshSec);
            sb.AppendLine("AlertTemp=" + AlertTemp);
            sb.AppendLine("TopMost=" + (TopMost ? "1" : "0"));
            sb.AppendLine("MascotImage=" + MascotImage);
            foreach (var a in CustomApps) sb.AppendLine("CustomApp=" + a);
            sb.AppendLine("WeatherMode=" + WeatherMode);
            sb.AppendLine("WeatherPlace=" + WeatherPlace);
            sb.AppendLine("WeatherLat=" + WeatherLat.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("WeatherLon=" + WeatherLon.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("AutoUpdate=" + (AutoUpdate ? "1" : "0"));
            sb.AppendLine("LastUpdateCheck=" + LastUpdateCheck);
            sb.AppendLine("AnnouncedVersion=" + AnnouncedVersion);
            sb.AppendLine("Language=" + Language);
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
        }

        // ---------- Autostart: a logon task with highest privileges, so no UAC prompt at each login ----------
        // One task per Windows account, so on a PC with several administrators each one's setting is their own.
        // Versions up to 1.0.0 used one shared name; that task is taken over by the account it belongs to.
        const string LegacyTaskName = "HPControlPanel";
        static string TaskName { get { return "HPControlPanel-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } }

        // Registered through the Task Scheduler COM API (no temp file another process could swap).
        static dynamic TaskFolder()
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            return svc.GetFolder("\\");
        }

        static dynamic FindTask(string name)
        {
            try { return TaskFolder().GetTask(name); } catch { return null; }
        }

        // The old shared task, but only when it runs as this account.
        static dynamic MyLegacyTask()
        {
            try
            {
                dynamic t = FindTask(LegacyTaskName);
                if (t == null) return null;
                string uid = Convert.ToString(t.Definition.Principal.UserId) ?? "";
                var me = System.Security.Principal.WindowsIdentity.GetCurrent();
                bool mine = uid.Equals(me.Name, StringComparison.OrdinalIgnoreCase) || uid.Equals(me.User.Value, StringComparison.OrdinalIgnoreCase)
                         || uid.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase);
                return mine ? t : null;
            }
            catch { return null; }
        }

        public static bool AutoStartEnabled()
        {
            return FindTask(TaskName) != null || MyLegacyTask() != null;
        }

        // Moves this account's old shared task to its own name, and re-points a task that runs another exe
        // (e.g. the old user-writable install) at this one.
        public static void RepairAutoStart()
        {
            try
            {
                if (MyLegacyTask() != null && SetAutoStart(true)) TaskFolder().DeleteTask(LegacyTaskName, 0);
                dynamic t = FindTask(TaskName);
                if (t == null) return;
                string path = t.Definition.Actions.Item(1).Path;
                if (!string.Equals(path, Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase)) SetAutoStart(true);
            }
            catch (Exception e) { App.Log(e); }
        }

        public static bool SetAutoStart(bool on)
        {
            try
            {
                if (!on)
                {
                    if (FindTask(TaskName) != null) TaskFolder().DeleteTask(TaskName, 0);
                    if (MyLegacyTask() != null) TaskFolder().DeleteTask(LegacyTaskName, 0);
                    return true;
                }
                // XML so the task also starts on battery (the simple onlogon defaults are AC-only).
                string exe = System.Security.SecurityElement.Escape(Process.GetCurrentProcess().MainModule.FileName);
                string user = System.Security.SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName);
                string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command></Exec></Actions>\r\n" +
                "</Task>\r\n";
                // 6 = TASK_CREATE_OR_UPDATE, 3 = TASK_LOGON_INTERACTIVE_TOKEN
                TaskFolder().RegisterTask(TaskName, xml, 6, null, null, 3, null);
                return true;
            }
            catch (Exception e) { App.Log(e); return false; }
        }

        // Start menu entry, so typing "hp" or "control" in Windows search finds the app.
        // install.ps1 puts it in the all-users Start menu; then the old per-user entry (which pointed at the
        // former user-writable install) is removed so search doesn't show a broken duplicate.
        public static void EnsureStartMenuShortcut()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HP Control Center.lnk");
                string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "HP Control Center.lnk");
                if (File.Exists(common))
                {
                    if (File.Exists(lnk))
                    {
                        File.Delete(lnk);
                        SHChangeNotify(0x00000004 /*SHCNE_DELETE*/, 0x0005, lnk, IntPtr.Zero);
                        SHChangeNotify(0x00001000 /*SHCNE_UPDATEDIR*/, 0x0005, Path.GetDirectoryName(lnk), IntPtr.Zero);
                    }
                    return;
                }
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                if (File.Exists(lnk) && string.Equals((string)sh.CreateShortcut(lnk).TargetPath, exe, StringComparison.OrdinalIgnoreCase)) return;
                dynamic s = sh.CreateShortcut(lnk);
                s.TargetPath = exe;
                s.WorkingDirectory = Path.GetDirectoryName(exe);
                s.IconLocation = exe + ",0";
                s.Description = "HP Control Center · HP 控制台";
                s.Save();
                // Tell Explorer so Start search picks the new entry up immediately.
                SHChangeNotify(0x00000002 /*SHCNE_CREATE*/, 0x0005 /*SHCNF_PATHW*/, lnk, IntPtr.Zero);
                SHChangeNotify(0x00001000 /*SHCNE_UPDATEDIR*/, 0x0005, Path.GetDirectoryName(lnk), IntPtr.Zero);
            }
            catch (Exception e) { App.Log(e); }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    }
}
