// CPU, memory, battery, brightness, volume, power mode and session actions.
using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace HPControlPanel
{
    public static class SystemInfo
    {
        // ---------- CPU usage ----------
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long lastIdle, lastTotal;

        public static int CpuUsage()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return -1;
            long total = kernel + user;
            long dIdle = idle - lastIdle, dTotal = total - lastTotal;
            lastIdle = idle; lastTotal = total;
            if (dTotal <= 0) return 0;
            return (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal);
        }

        // ---------- Memory ----------
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        public static void Memory(out double usedGb, out double totalGb)
        {
            var m = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(m);
            totalGb = m.ullTotalPhys / 1073741824.0;
            usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        // ---------- Battery ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        public static void Battery(out int percent, out bool pluggedIn, out bool charging)
        {
            SYSTEM_POWER_STATUS s;
            GetSystemPowerStatus(out s);
            percent = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
            pluggedIn = s.ACLineStatus == 1;
            charging = (s.BatteryFlag & 8) != 0;
        }

        // ---------- ACPI thermal zone (fallback CPU temperature on boards without an EcProfile) ----------
        public static int AcpiTemp()
        {
            try
            {
                int best = -1;
                using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                    foreach (ManagementObject mo in q.Get())
                    {
                        int c = (int)Math.Round(Convert.ToDouble(mo["CurrentTemperature"]) / 10.0 - 273.15);
                        if (c > 15 && c < 125 && c > best) best = c;
                    }
                return best;
            }
            catch { return -1; }
        }

        // ---------- Brightness (laptop panel, via WMI) ----------
        public static int GetBrightness()
        {
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject mo in q.Get()) return Convert.ToInt32(mo["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public static void SetBrightness(int value)
        {
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject mo in q.Get()) mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)value });
            }
            catch { }
        }

        // ---------- Volume (Core Audio) ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr p);
            int UnregisterControlChangeNotify(IntPtr p);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float db, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float db);
            int GetChannelVolumeLevelScalar(uint ch, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // flow 0 = playback (speakers), 1 = capture (microphone)
        static IAudioEndpointVolume Endpoint(int flow = 0)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(flow, flow == 0 ? 1 /*multimedia*/ : 0 /*console*/, out dev) != 0) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) return null;
                return (IAudioEndpointVolume)o;
            }
            catch { return null; }
        }

        public static int GetVolume(out bool muted)
        {
            muted = false;
            var ep = Endpoint();
            if (ep == null) return -1;
            float v; ep.GetMasterVolumeLevelScalar(out v); ep.GetMute(out muted);
            return (int)Math.Round(v * 100);
        }

        public static void SetVolume(int value)
        {
            var ep = Endpoint();
            if (ep == null) return;
            var g = Guid.Empty;
            ep.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, value)) / 100f, ref g);
        }

        public static void SetMute(bool mute)
        {
            var ep = Endpoint();
            if (ep == null) return;
            var g = Guid.Empty;
            ep.SetMute(mute, ref g);
        }

        // Default microphone: true = on (not muted), null = no microphone.
        public static bool? MicOn()
        {
            var ep = Endpoint(1);
            if (ep == null) return null;
            bool muted; ep.GetMute(out muted);
            return !muted;
        }

        public static void SetMic(bool on)
        {
            var ep = Endpoint(1);
            if (ep == null) return;
            var g = Guid.Empty;
            ep.SetMute(!on, ref g);
        }

        // ---------- Power mode (Windows 10/11 power slider overlay, works on any laptop using the Balanced plan) ----------
        [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);

        public static readonly Guid PowerSaver = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid PowerBalanced = Guid.Empty;
        public static readonly Guid PowerPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        public static Guid GetPowerMode()
        {
            Guid g;
            return PowerGetEffectiveOverlayScheme(out g) == 0 ? g : Guid.Empty;
        }

        public static bool SetPowerMode(Guid g) { return PowerSetActiveOverlayScheme(g) == 0; }

        // ---------- Power plans (控制面板 → 电源选项) ----------
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subGroup, uint accessFlags, uint index, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subGroup, IntPtr setting, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr active);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr mem);
        const uint ACCESS_SCHEME = 16;

        // Built-in plans get names in the interface language; plans the user created keep their own name.
        static string PlanName(Guid g)
        {
            switch (g.ToString())
            {
                case "381b4222-f694-41f0-9685-ff5bb260df2e": return L.S("平衡", "Balanced");
                case "a1841308-3541-4fab-bc81-f71556f20b4a": return L.S("节能", "節能", "Power saver");
                case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": return L.S("高性能", "高效能", "High performance");
                case "e9a42b02-d5df-448d-aa00-03f14749eb61": return L.S("卓越性能", "極致效能", "Ultimate performance");
            }
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0) return g.ToString();
            var buf = new byte[size];
            if (PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return g.ToString();
            return System.Text.Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
        }

        public static System.Collections.Generic.List<Tuple<Guid, string>> PowerPlans()
        {
            var list = new System.Collections.Generic.List<Tuple<Guid, string>>();
            var buf = new byte[16];
            for (uint i = 0; ; i++)
            {
                uint size = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buf, ref size) != 0) break;
                var g = new Guid(buf);
                list.Add(Tuple.Create(g, PlanName(g)));
            }
            return list;
        }

        public static Guid ActivePlan()
        {
            IntPtr p;
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
            var g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            LocalFree(p);
            return g;
        }

        public static bool SetPlan(Guid g) { return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0; }

        // Windows applies the power-mode overlay (省电 / 平衡 / 高性能) only while the Balanced plan is active.
        public static readonly Guid BalancedPlan = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static bool PowerModeAvailable()
        {
            Guid g = ActivePlan();
            return g == Guid.Empty || g == BalancedPlan;
        }

        // ---------- Session actions ----------
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("powrprof.dll")] static extern bool IsPwrSuspendAllowed();
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static void Lock() { LockWorkStation(); }

        // Classic S3 machines: suspend. Modern Standby machines (no S1-S3): turning the display off is what enters standby.
        public static void Sleep()
        {
            if (IsPwrSuspendAllowed()) SetSuspendState(false, false, false);
            else SendMessage((IntPtr)0xFFFF, 0x0112 /*WM_SYSCOMMAND*/, (IntPtr)0xF170 /*SC_MONITORPOWER*/, (IntPtr)2);
        }

        public static void Restart() { Run("shutdown.exe", "/r /t 0"); }
        public static void Shutdown() { Run("shutdown.exe", "/s /t 0"); }

        static void Run(string exe, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi);
        }
    }
}
