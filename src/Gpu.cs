// GPU temperature from the graphics driver, the same source Task Manager uses (WDDM 2.4+ adapter perf data).
// Discrete GPUs (NVIDIA / AMD / Intel Arc) usually report it; integrated GPUs usually don't.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace HPControlPanel
{
    public static class Gpu
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }

        [StructLayout(LayoutKind.Sequential)]
        struct ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

        [StructLayout(LayoutKind.Sequential)]
        struct QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct CLOSEADAPTER { public uint hAdapter; }

        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref QUERYADAPTERINFO q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref CLOSEADAPTER c);

        const int KMTQAITYPE_ADAPTERPERFDATA = 62;
        // D3DKMT_ADAPTER_PERFDATA: UINT PhysicalAdapterIndex; 5 x ULONGLONG; ULONG FanRPM (48); ULONG Power (52); ULONG Temperature (56, deci-°C); UCHAR ...
        const int PerfDataSize = 64, FanOffset = 48, TempOffset = 56;

        // Highest temperature any adapter reports, in °C; -1 when no driver reports one.
        public static int DriverTemp(out int fanRpm)
        {
            fanRpm = -1;
            int best = -1;
            try
            {
                var e = new ENUMADAPTERS2();
                if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return -1;
                int sz = Marshal.SizeOf(typeof(ADAPTERINFO));
                e.pAdapters = Marshal.AllocHGlobal(sz * (int)e.NumAdapters);
                IntPtr perf = Marshal.AllocHGlobal(PerfDataSize);
                try
                {
                    if (D3DKMTEnumAdapters2(ref e) != 0) return -1;
                    for (int i = 0; i < e.NumAdapters; i++)
                    {
                        var info = (ADAPTERINFO)Marshal.PtrToStructure(e.pAdapters + i * sz, typeof(ADAPTERINFO));
                        for (int k = 0; k < PerfDataSize; k++) Marshal.WriteByte(perf, k, 0);
                        var q = new QUERYADAPTERINFO { hAdapter = info.hAdapter, Type = KMTQAITYPE_ADAPTERPERFDATA, pPrivateDriverData = perf, PrivateDriverDataSize = PerfDataSize };
                        if (D3DKMTQueryAdapterInfo(ref q) == 0)
                        {
                            int t = (int)Math.Round(Marshal.ReadInt32(perf, TempOffset) / 10.0);
                            int f = Marshal.ReadInt32(perf, FanOffset);
                            if (t > 0 && t < 125 && t > best) { best = t; fanRpm = f > 0 && f < 20000 ? f : -1; }
                        }
                        var c = new CLOSEADAPTER { hAdapter = info.hAdapter };
                        D3DKMTCloseAdapter(ref c);
                    }
                }
                finally { Marshal.FreeHGlobal(e.pAdapters); Marshal.FreeHGlobal(perf); }
            }
            catch { return -1; }
            return best;
        }

        // ---------- Usage and video memory: the "GPU Engine" / "GPU Adapter Memory" counters Task Manager uses ----------
        // English counter names via PdhAddEnglishCounter, so this works on Chinese (or any) Windows.
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

        const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000, PDH_MORE_DATA = 0x800007D2, PDH_CSTATUS_NEW_DATA = 1;
        static IntPtr query, cEngine, cDedicated, cShared;
        static bool countersOk;

        public static bool InitCounters()
        {
            if (countersOk) return true;
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return false;
            countersOk = PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out cEngine) == 0
                       & PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out cDedicated) == 0
                       & PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Shared Usage", IntPtr.Zero, out cShared) == 0;
            if (countersOk) PdhCollectQueryData(query);   // utilization is a rate: the first sample only primes it
            return countersOk;
        }

        // Null when the values couldn't be read (as opposed to an empty, idle reading).
        static Dictionary<string, double> Read(IntPtr counter)
        {
            const uint fmt = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
            // Instances (one per process x engine) come and go; if the list grows between the size query
            // and the data call, PDH answers PDH_MORE_DATA again, so ask for the size again and retry.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint size = 0, count;
                uint rc = PdhGetFormattedCounterArray(counter, fmt, ref size, out count, IntPtr.Zero);
                if (rc == 0) return new Dictionary<string, double>();   // no instances at all
                if (rc != PDH_MORE_DATA) return null;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    rc = PdhGetFormattedCounterArray(counter, fmt, ref size, out count, buf);
                    if (rc == PDH_MORE_DATA) continue;
                    if (rc != 0) return null;
                    var result = new Dictionary<string, double>();
                    // PDH_FMT_COUNTERVALUE_ITEM_W: LPWSTR name (8) + { DWORD status (4) + pad (4) + double (8) }
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr item = buf + i * 24;
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                        uint status = (uint)Marshal.ReadInt32(item, 8);
                        // PDH_CSTATUS_VALID_DATA (0) and PDH_CSTATUS_NEW_DATA (1) both carry a valid value.
                        if (status > PDH_CSTATUS_NEW_DATA || name == null) continue;
                        double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                        double old; result.TryGetValue(name, out old);
                        result[name] = old + v;
                    }
                    return result;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }

        // "pid_123_luid_0x00000000_0x0000D1B6_phys_0_eng_3_engtype_3D" -> adapter "luid_0x00000000_0x0000D1B6_phys_0"
        static string Adapter(string instance)
        {
            int a = instance.IndexOf("luid_");
            if (a < 0) return instance;
            int b = instance.IndexOf("_eng_", a);
            return b > a ? instance.Substring(a, b - a) : instance.Substring(a);
        }

        // Usage %: like Task Manager, the busiest engine type of the busiest adapter.
        // Memory: bytes of dedicated and shared GPU memory in use, for the adapter using the most dedicated memory.
        public static void Usage(out int percent, out double dedicatedGb, out double sharedGb)
        {
            percent = -1; dedicatedGb = sharedGb = -1;
            if (!countersOk || PdhCollectQueryData(query) != 0) return;

            var engines = Read(cEngine);
            if (engines == null) return;   // shows "—" for one poll instead of a false 0%
            var perEngineType = new Dictionary<string, double>();
            foreach (var kv in engines)
            {
                int t = kv.Key.IndexOf("engtype_");
                string key = Adapter(kv.Key) + "|" + (t >= 0 ? kv.Key.Substring(t) : "");
                double old; perEngineType.TryGetValue(key, out old);
                perEngineType[key] = old + kv.Value;
            }
            double best = 0;
            foreach (var v in perEngineType.Values) best = Math.Max(best, v);
            percent = (int)Math.Round(Math.Min(100, best));

            var ded = Read(cDedicated);
            var sh = Read(cShared);
            if (ded == null || sh == null) return;
            // A discrete GPU shows up as real dedicated usage; integrated GPUs use (almost) only shared memory.
            string pick = null; double max = -1;
            foreach (var kv in ded) if (kv.Value > max) { max = kv.Value; pick = kv.Key; }
            if (max < 64 * 1048576.0)
            {
                max = -1;
                foreach (var kv in sh) if (kv.Value > max) { max = kv.Value; pick = kv.Key; }
            }
            if (pick == null) return;
            double d, s;
            ded.TryGetValue(pick, out d); sh.TryGetValue(pick, out s);
            dedicatedGb = d / 1073741824.0;
            sharedGb = s / 1073741824.0;
        }

        // Dedicated video memory size from the display driver's registry entry (largest adapter), in GB; 0 if unknown.
        public static double DedicatedTotalGb()
        {
            double best = 0;
            try
            {
                using (var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = cls.OpenSubKey(sub))
                            {
                                object v = k == null ? null : k.GetValue("HardwareInformation.qwMemorySize") ?? k.GetValue("HardwareInformation.MemorySize");
                                double bytes = v is long ? (long)v : v is int ? (uint)(int)v : v is byte[] && ((byte[])v).Length >= 4 ? BitConverter.ToUInt32((byte[])v, 0) : 0;
                                best = Math.Max(best, bytes / 1073741824.0);
                            }
                        }
                        catch { }
                    }
            }
            catch { }
            return best;
        }

        // True when every GPU is integrated into the CPU (Intel UHD/Iris/HD, AMD APU "Radeon Graphics").
        // Discrete cards by name. Everything else is treated as integrated, so new iGPU names
        // ("Intel Arc 140V GPU", "Radeon 890M") don't show up as a separate graphics card.
        // "Intel Arc Graphics" (Core Ultra) and "Radeon RX Vega" are integrated.
        public const string DiscretePattern = @"NVIDIA|GeForce|Quadro|\bRTX\b|Radeon\s+RX\s*\d{4}|Radeon\s+Pro|\bArc\s+A\d{3}";

        public static bool IsDiscrete(string gpuName) { return gpuName != null && Regex.IsMatch(gpuName, DiscretePattern, RegexOptions.IgnoreCase); }

        public static bool HasDiscrete(string gpuList)
        {
            return (gpuList ?? "").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries).Any(IsDiscrete);
        }

        // At least one GPU was found and none of them is discrete.
        public static bool AllIntegrated(string gpuList)
        {
            return !string.IsNullOrEmpty(gpuList) && !HasDiscrete(gpuList);
        }
    }
}
