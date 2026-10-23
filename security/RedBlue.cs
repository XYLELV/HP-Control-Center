// Red team vs blue team: a test program that attacks HP Control Center's own defenses, to verify the
// privilege-escalation fixes from the v1.0.1 review actually hold. It runs as an ordinary (medium-integrity)
// user, the same attacker the threat model assumes, and reports BLOCKED / ALLOWED for each vector.
//
//   Blue team: SafeFile (reparse-point / link guard), Settings.InstalledCopy, SystemInfo.Sys32.
//   Red team : the four attacks the review flagged, aimed only at this app's own data folder and helpers.
//
// It changes nothing outside its own temp playground and never touches another machine or another program.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using HPControlPanel;

static class RedBlue
{
    static int failures;

    static void Result(string vector, bool blocked, string detail)
    {
        Console.WriteLine((blocked ? "[BLOCKED] " : "[ALLOWED] ") + vector);
        Console.WriteLine("          " + detail);
        if (!blocked) failures++;
    }

    static void Mklink(string args)
    {
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink " + args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
        {
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        }
    }

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "hpcp-redblue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Red team vs blue team — HP Control Center " + Updater.Version);
        Console.WriteLine("playground: " + root);
        Console.WriteLine();

        try
        {
            // ---- Vector 1: junction the data folder, then make the elevated app write through it ----
            // Attack: a same-user process replaces %APPDATA%\HPControlPanel with a junction to a folder it wants
            // the admin write to land in. Defense: SafeFile.DirOk sees the data folder no longer resolves to itself.
            string data = Path.Combine(root, "data"), elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            Mklink("/J \"" + data + "\" \"" + elsewhere + "\"");
            bool wrote = SafeFile.WriteAllText(Path.Combine(data, "settings.ini"), "Theme=pwned");
            bool landed = File.Exists(Path.Combine(elsewhere, "settings.ini"));
            Result("Junction redirect of the data folder",
                !wrote && !landed,
                "SafeFile.WriteAllText returned " + wrote + "; file in the redirect target: " + landed);

            // ---- Vector 2: hard-link a data file to a protected file, then make the app overwrite it ----
            // Attack: settings.ini is pre-created as a hard link to, say, a file in another user's folder, so the
            // admin write overwrites that file. Defense: SafeFile opens the file and refuses when link count > 1.
            string realData = Path.Combine(root, "realdata");
            Directory.CreateDirectory(realData);
            string secret = Path.Combine(root, "secret.txt");
            File.WriteAllText(secret, "original-protected-content");
            Mklink("/H \"" + Path.Combine(realData, "settings.ini") + "\" \"" + secret + "\"");
            bool wrote2 = SafeFile.WriteAllText(Path.Combine(realData, "settings.ini"), "Theme=pwned");
            string secretNow = File.ReadAllText(secret);
            Result("Hard link from a data file to a protected file",
                !wrote2 && secretNow == "original-protected-content",
                "SafeFile.WriteAllText returned " + wrote2 + "; protected file now: \"" + secretNow + "\"");

            // ---- Vector 3: plant a fake helper in the current directory ----
            // Attack: drop pnputil.exe / shutdown.exe where the elevated app runs, hoping a bare-name launch finds
            // it first. Defense: the app launches helpers by their full System32 path, so the plant is never found.
            string pnp = SystemInfo.Sys32("pnputil.exe"), shut = SystemInfo.Sys32("shutdown.exe");
            string sys32 = Environment.SystemDirectory;
            bool fullPaths = Path.IsPathRooted(pnp) && pnp.StartsWith(sys32, StringComparison.OrdinalIgnoreCase)
                          && Path.IsPathRooted(shut) && shut.StartsWith(sys32, StringComparison.OrdinalIgnoreCase);
            Result("Planted helper exe in the working directory",
                fullPaths,
                "helpers resolve to: " + pnp + " / " + shut);

            // ---- Vector 4: a portable (user-writable) copy tries to register the elevated autostart task ----
            // Attack: run the exe from a user-writable folder (e.g. Downloads) and turn on autostart; a highest-
            // privilege logon task would then launch a swappable exe at sign-in. Defense: Settings.InstalledCopy
            // is false outside Program Files, and SetAutoStart(true) refuses. (This test program is itself running
            // from %TEMP%, so InstalledCopy reflects exactly that portable case.)
            var installed = (bool)typeof(Settings).GetProperty("InstalledCopy", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            bool registered = Settings.SetAutoStart(true);
            if (registered) { try { Settings.SetAutoStart(false); } catch { } }   // undo if the guard somehow failed
            Result("Portable copy registering elevated autostart",
                !installed && !registered,
                "InstalledCopy=" + installed + " (running from " + Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "); SetAutoStart(true) returned " + registered);
        }
        finally
        {
            // Remove the junction without following it, then the playground.
            try { foreach (var d in Directory.GetDirectories(root)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "BLUE TEAM HELD: all 4 vectors blocked."
            : "RED TEAM GOT THROUGH on " + failures + " vector(s) — see [ALLOWED] above.");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
