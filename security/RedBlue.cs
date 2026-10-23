// Red team vs blue team: a test program that attacks HP Control Center's own defenses, to verify the
// privilege-escalation fixes from the v1.0.1 review actually hold. It runs as an ordinary (medium-integrity)
// user, the same attacker the threat model assumes, and reports BLOCKED / ALLOWED / INCONCLUSIVE per vector.
//
//   Blue team: SafeFile (reparse-point / link guard), Settings.InstalledCopy, SystemInfo.Sys32.
//   Red team : the four attacks the review flagged, aimed only at this app's own data folder and helpers.
//
// Scope and honesty:
//   - Everything happens inside one temp playground. Nothing is written outside it, no other program or machine
//     is touched, and NO system-wide state (Task Scheduler, registry, services) is changed — vector 4 only reads
//     the guard that runs before any task would be registered, it never calls the registration.
//   - Each attack first checks that its precondition really took effect (the junction / hard link was actually
//     created). If not, it reports INCONCLUSIVE rather than counting a failed attack as a blocked one.
using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using HPControlPanel;

static class RedBlue
{
    static int broke, inconclusive;

    enum R { Blocked, Allowed, Inconclusive }

    static void Report(string vector, R r, string detail)
    {
        Console.WriteLine("[" + r.ToString().ToUpperInvariant() + "] " + vector);
        Console.WriteLine("          " + detail);
        if (r == R.Allowed) broke++;
        else if (r == R.Inconclusive) inconclusive++;
    }

    static void Mklink(string args)
    {
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink " + args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
        { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); }
    }

    // Link count, so vector 2 can confirm the hard link really exists before trusting the result.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION i);
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct BY_HANDLE_FILE_INFORMATION { public uint Attributes; public long C, A, W; public uint Vol, SizeHi, SizeLo, Links, IdxHi, IdxLo; }

    static uint LinkCount(string path)
    {
        using (var h = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            BY_HANDLE_FILE_INFORMATION i;
            return !h.IsInvalid && GetFileInformationByHandle(h, out i) ? i.Links : 0;
        }
    }

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "hpcp-redblue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Red team vs blue team - HP Control Center " + Updater.Version);
        Console.WriteLine("playground: " + root);
        Console.WriteLine("(temp folder only; no system-wide change; no task is ever registered)");
        Console.WriteLine();

        try
        {
            // ---- Vector 1: junction the data folder, then make the elevated app write through it ----
            string data = Path.Combine(root, "data"), elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            Mklink("/J \"" + data + "\" \"" + elsewhere + "\"");
            bool junctionReady = Directory.Exists(data) && (File.GetAttributes(data) & FileAttributes.ReparsePoint) != 0;
            if (!junctionReady)
                Report("Junction redirect of the data folder", R.Inconclusive, "could not create the junction (symlink/junction privilege?) - attack not set up, result means nothing");
            else
            {
                bool wrote = SafeFile.WriteAllText(Path.Combine(data, "settings.ini"), "Theme=pwned");
                bool landed = File.Exists(Path.Combine(elsewhere, "settings.ini"));
                Report("Junction redirect of the data folder", wrote || landed ? R.Allowed : R.Blocked,
                    "junction confirmed; SafeFile.WriteAllText=" + wrote + "; file in redirect target=" + landed);
            }

            // ---- Vector 2: hard-link a data file to a protected file, then make the app overwrite it ----
            string realData = Path.Combine(root, "realdata");
            Directory.CreateDirectory(realData);
            string secret = Path.Combine(root, "secret.txt");
            File.WriteAllText(secret, "original-protected-content");
            string linked = Path.Combine(realData, "settings.ini");
            Mklink("/H \"" + linked + "\" \"" + secret + "\"");
            bool linkReady = File.Exists(linked) && LinkCount(secret) > 1;
            if (!linkReady)
                Report("Hard link from a data file to a protected file", R.Inconclusive, "hard link not created (link count " + LinkCount(secret) + ") - attack not set up, result means nothing");
            else
            {
                bool wrote2 = SafeFile.WriteAllText(linked, "Theme=pwned");
                string secretNow = File.ReadAllText(secret);
                Report("Hard link from a data file to a protected file", wrote2 || secretNow != "original-protected-content" ? R.Allowed : R.Blocked,
                    "hard link confirmed (link count " + LinkCount(secret) + "); SafeFile.WriteAllText=" + wrote2 + "; protected file now=\"" + secretNow + "\"");
            }

            // ---- Vector 3: plant a fake helper in the current directory ----
            // Pure path check, no precondition: does the app resolve helpers to System32 rather than a bare name?
            string pnp = SystemInfo.Sys32("pnputil.exe"), shut = SystemInfo.Sys32("shutdown.exe");
            string sys32 = Environment.SystemDirectory;
            bool full = Path.IsPathRooted(pnp) && pnp.StartsWith(sys32, StringComparison.OrdinalIgnoreCase)
                     && Path.IsPathRooted(shut) && shut.StartsWith(sys32, StringComparison.OrdinalIgnoreCase);
            Report("Planted helper exe in the working directory", full ? R.Blocked : R.Allowed, "helpers resolve to: " + pnp + " / " + shut);

            // ---- Vector 4: a portable copy must not be allowed to register the elevated autostart task ----
            // Side-effect-free: SetAutoStart(true)'s first statement is `if (!InstalledCopy) return false`, so the
            // guard decides before any task is registered. We read that guard only; we never call the registration.
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            bool installed = (bool)typeof(Settings).GetProperty("InstalledCopy", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            if (installed)
                Report("Portable copy registering elevated autostart", R.Inconclusive, "this copy IS in Program Files (" + exeDir + ") - run the portable copy from a user folder to test the portable case");
            else
                Report("Portable copy registering elevated autostart", R.Blocked, "InstalledCopy=False for " + exeDir + "; the autostart guard refuses before any task is registered (no Task Scheduler call made)");
        }
        finally
        {
            try { foreach (var d in Directory.GetDirectories(root)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }

        Console.WriteLine();
        if (broke > 0) Console.WriteLine("RED TEAM GOT THROUGH on " + broke + " vector(s) - see [ALLOWED] above.");
        else if (inconclusive > 0) Console.WriteLine("BLUE TEAM HELD on every vector that ran; " + inconclusive + " were INCONCLUSIVE (attack could not be set up).");
        else Console.WriteLine("BLUE TEAM HELD: all 4 vectors blocked.");
        Environment.Exit(broke > 0 ? 1 : 0);
    }
}
