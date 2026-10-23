// Red team vs blue team: a test program that attacks HP Control Center's own defenses, to verify the
// privilege-escalation fixes from the v1.0.1 review actually hold. It runs as an ordinary (medium-integrity)
// user, the same attacker the threat model assumes, and reports BLOCKED / ALLOWED / INCONCLUSIVE per vector.
//
//   Blue team: SafeFile (reparse-point / link guard), Settings.InstalledCopy, SystemInfo.Sys32.
//   Red team : the attacks the reviews flagged, aimed only at this app's own data folder and helpers.
//
// Scope and honesty:
//   - Everything happens inside one temp playground. Nothing is written outside it, no other program or machine
//     is touched, and NO system-wide state (Task Scheduler, registry, services) is changed — vector 4 only calls
//     the guard that runs before any task would be registered, it never calls the registration.
//   - Each attack first checks that its precondition really took effect (the junction / hard link was actually
//     created). If not, it reports INCONCLUSIVE rather than counting a failed attack as a blocked one.
//   - A control run first checks that SafeFile still does its normal job (write, overwrite, append, read a plain file),
//     so a guard that simply refuses everything can't pass, and that this test isn't itself running elevated.
//   - Verdicts rest on what actually happened to the target (sentinel content, files left behind), not on return values.
//   - Exit code: 0 = all held, 1 = an attack got through, 2 = something was INCONCLUSIVE, 3 = the control run failed.
using System;
using System.IO;
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

    static bool IsElevated()
    {
        var p = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
        return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "hpcp-redblue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Red team vs blue team - HP Control Center " + Updater.Version);
        Console.WriteLine("playground: " + root);
        Console.WriteLine("(temp folder only; no system-wide change; no task is ever registered)");
        Console.WriteLine();

        if (IsElevated())
        {
            Console.WriteLine("[CONTROL FAILED] this test is running elevated; the attacker is an ordinary user. Run it from a normal (non-admin) terminal.");
            Environment.Exit(3);
        }

        try
        {
            // ---- Control: the guard must still allow ordinary writes and reads, or "blocked" below means nothing ----
            string plainDir = Path.Combine(root, "plain"), plain = Path.Combine(plainDir, "settings.ini");
            Directory.CreateDirectory(plainDir);
            bool ok = SafeFile.WriteAllText(plain, "Theme=one") && SafeFile.WriteAllText(plain, "Theme=two")
                   && SafeFile.AppendAllText(plain, "\nTopMost=1") && File.ReadAllText(plain) == "Theme=two\nTopMost=1";
            byte[] back = SafeFile.ReadAllBytes(plain, 1 << 20);
            ok = ok && back != null && back.Length == new FileInfo(plain).Length && SafeFile.ReadAllBytes(plain, 4) == null;
            if (!ok)
            {
                Console.WriteLine("[CONTROL FAILED] SafeFile refused or garbled an ordinary write/read - the vectors below would prove nothing.");
                Environment.Exit(3);
            }
            Console.WriteLine("[CONTROL OK] ordinary write / overwrite / append / read work; size cap enforced; not elevated");
            Console.WriteLine();

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
                Report("Junction redirect of the data folder", landed ? R.Allowed : wrote ? R.Inconclusive : R.Blocked,
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
                // Verdict on the protected file itself: a safe implementation may replace the link with a new file
                // (write succeeds) as long as the protected content is untouched.
                Report("Hard link from a data file to a protected file", secretNow != "original-protected-content" ? R.Allowed : R.Blocked,
                    "hard link confirmed (link count " + LinkCount(secret) + "); SafeFile.WriteAllText=" + wrote2 + "; protected file now=\"" + secretNow + "\"");
            }

            // ---- Vector 3: plant a fake helper in the current directory ----
            // Pure path check, no precondition: does the app resolve helpers to System32 rather than a bare name?
            string pnp = SystemInfo.Sys32("pnputil.exe"), shut = SystemInfo.Sys32("shutdown.exe");
            string sys32 = Environment.SystemDirectory;
            bool full = Path.IsPathRooted(pnp) && pnp.StartsWith(sys32, StringComparison.OrdinalIgnoreCase)
                     && Path.IsPathRooted(shut) && shut.StartsWith(sys32, StringComparison.OrdinalIgnoreCase);
            Report("Planted helper exe in the working directory", full ? R.Blocked : R.Allowed, "helpers resolve to: " + pnp + " / " + shut);

            // ---- Vector 4: only the exact installed path may register the elevated autostart task ----
            // Side-effect-free: SetAutoStart(true) refuses unless Settings.IsInstallPath(own exe). We call that guard
            // with the paths an attacker could use; we never call the registration.
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var tries = new[]
            {
                Path.Combine(root, "HPControlPanel.exe"),                               // portable copy in a user folder
                Path.Combine(pf, "SomeVendor", "HPControlPanel.exe"),                   // another Program Files folder (may be user-writable)
                Path.Combine(pf, "HPControlPanel", "..", "SomeVendor", "HPControlPanel.exe"),
                Path.Combine(pf, "HPControlPanel", "sub", "HPControlPanel.exe"),
            };
            string accepted = null;
            foreach (var t in tries) if (Settings.IsInstallPath(t)) { accepted = t; break; }
            bool exactOk = !Directory.Exists(Path.GetDirectoryName(Settings.InstallExe)) || Settings.IsInstallPath(Settings.InstallExe);
            if (accepted != null)
                Report("Non-installed copy registering elevated autostart", R.Allowed, "guard accepted " + accepted);
            else if (!exactOk)
                Report("Non-installed copy registering elevated autostart", R.Inconclusive, "guard also refuses the real install path " + Settings.InstallExe + " - control failed");
            else
                Report("Non-installed copy registering elevated autostart", R.Blocked, "guard refuses portable / other-Program-Files / ..-escaped / subfolder paths; accepts only " + Settings.InstallExe);

            // ---- Vector 5: junction a folder the elevated app reads from (legacy migration), to copy out a file ----
            string legacy = Path.Combine(root, "legacy"), hidden = Path.Combine(root, "hidden");
            Directory.CreateDirectory(hidden);
            File.WriteAllText(Path.Combine(hidden, "settings.ini"), "secret-should-not-be-read");
            Mklink("/J \"" + legacy + "\" \"" + hidden + "\"");
            if (!(Directory.Exists(legacy) && (File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0))
                Report("Junction redirect of a folder the app reads from", R.Inconclusive, "could not create the junction - attack not set up, result means nothing");
            else
            {
                byte[] got = SafeFile.ReadAllBytes(Path.Combine(legacy, "settings.ini"), 1 << 20);
                Report("Junction redirect of a folder the app reads from", got != null ? R.Allowed : R.Blocked,
                    "junction confirmed; SafeFile.ReadAllBytes returned " + (got == null ? "null (refused)" : got.Length + " bytes"));
            }

            // ---- Vector 6: hard link in a folder the app reads from, pointing at another file ----
            string readDir = Path.Combine(root, "readdir");
            Directory.CreateDirectory(readDir);
            string other = Path.Combine(root, "other.txt"), viaLink = Path.Combine(readDir, "custom-mascot.png");
            File.WriteAllText(other, "secret-should-not-be-read");
            Mklink("/H \"" + viaLink + "\" \"" + other + "\"");
            if (!(File.Exists(viaLink) && LinkCount(other) > 1))
                Report("Hard link in a folder the app reads from", R.Inconclusive, "hard link not created - attack not set up, result means nothing");
            else
            {
                byte[] got = SafeFile.ReadAllBytes(viaLink, 1 << 20);
                Report("Hard link in a folder the app reads from", got != null ? R.Allowed : R.Blocked,
                    "hard link confirmed; SafeFile.ReadAllBytes returned " + (got == null ? "null (refused)" : got.Length + " bytes"));
            }
        }
        finally
        {
            try { foreach (var d in Directory.GetDirectories(root)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }

        Console.WriteLine();
        if (broke > 0) Console.WriteLine("RED TEAM GOT THROUGH on " + broke + " vector(s) - see [ALLOWED] above.");
        else if (inconclusive > 0) Console.WriteLine("BLUE TEAM HELD on every vector that ran; " + inconclusive + " were INCONCLUSIVE (attack could not be set up).");
        else Console.WriteLine("BLUE TEAM HELD: all 6 vectors blocked.");
        Environment.Exit(broke > 0 ? 1 : inconclusive > 0 ? 2 : 0);
    }
}
