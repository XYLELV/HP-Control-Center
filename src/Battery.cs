// Battery details straight from the battery driver (IOCTL_BATTERY_*), which works on any laptop:
// design / full-charge capacity (health), cycle count, live capacity, voltage, charge/discharge rate and
// Windows' own remaining-time estimate. Falls back to an estimate from capacity changes when the rate is unknown.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HPControlPanel
{
    public class BatteryReading
    {
        public bool Present;
        public int DesignMWh = -1, FullMWh = -1, CycleCount = -1;
        public int NowMWh = -1, VoltageMV = -1;
        public int RateMW;                 // + charging, - discharging, 0 idle/unknown
        public bool Online, Charging, Discharging;
        public string RateSource = "";     // where RateMW came from
        public int RemainingMin = -1;      // time left on battery
        public int ToFullMin = -1;         // time until full while charging
        public double HealthPercent { get { return DesignMWh > 0 && FullMWh > 0 ? 100.0 * FullMWh / DesignMWh : -1; } }
    }

    public static class Battery
    {
        static readonly Guid BatteryClass = new Guid("72631E54-78A4-11D0-BCF7-00AA00B7B32A");
        const uint IOCTL_BATTERY_QUERY_TAG = 0x294040, IOCTL_BATTERY_QUERY_INFORMATION = 0x294044, IOCTL_BATTERY_QUERY_STATUS = 0x29404C;
        const int BatteryInformation = 0, BatteryEstimatedTime = 3;
        const uint UNKNOWN = 0xFFFFFFFF;
        const int RATE_UNKNOWN = unchecked((int)0x80000000);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid cls, int index, ref SP_DEVICE_INTERFACE_DATA data);
        // Unicode (…DetailW): the path is read back as UTF-16 below.
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        // First battery's device path, or null on desktops.
        static string DevicePath()
        {
            var cls = BatteryClass;
            IntPtr set = SetupDiGetClassDevs(ref cls, IntPtr.Zero, IntPtr.Zero, 0x12 /*DIGCF_PRESENT | DIGCF_DEVICEINTERFACE*/);
            if (set == new IntPtr(-1)) return null;
            try
            {
                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref cls, 0, ref did)) return null;
                int need;
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);   // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, out need, IntPtr.Zero)) return null;
                    return Marshal.PtrToStringUni(buf + 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        static byte[] Ioctl(SafeFileHandle h, uint code, byte[] input, int outSize)
        {
            var output = new byte[outSize];
            int got;
            return DeviceIoControl(h, code, input, input.Length, output, outSize, out got, IntPtr.Zero) ? output : null;
        }

        static byte[] Query(SafeFileHandle h, uint tag, int level, int outSize)
        {
            var q = new byte[12];   // BATTERY_QUERY_INFORMATION: tag, level, AtRate
            BitConverter.GetBytes(tag).CopyTo(q, 0);
            BitConverter.GetBytes(level).CopyTo(q, 4);
            return Ioctl(h, IOCTL_BATTERY_QUERY_INFORMATION, q, outSize);
        }

        // Capacity samples for estimating the rate when the driver reports it as unknown.
        static readonly List<KeyValuePair<DateTime, int>> samples = new List<KeyValuePair<DateTime, int>>();
        static bool lastCharging;
        static double emaRateMW;          // smoothed rate the times are computed from
        static string cachedPath;         // SetupAPI enumeration is the slow part; do it once
        static bool pathLooked;

        // knownRateMW: a live rate from another source (the EC's smart-battery current), or null.
        // adjust: corrections from another source (the EC's own capacities), applied before the times are computed.
        public static BatteryReading Read(int? knownRateMW = null, Action<BatteryReading> adjust = null)
        {
            var r = new BatteryReading();
            try
            {
                if (!pathLooked) { cachedPath = DevicePath(); pathLooked = true; }
                string path = cachedPath;
                if (path == null) return r;
                using (var h = CreateFile(path, 0xC0000000 /*GENERIC_READ|WRITE*/, 3, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0x80, IntPtr.Zero))
                {
                    if (h.IsInvalid) { pathLooked = false; return r; }   // battery swapped / removed: look again next time
                    var tagBuf = Ioctl(h, IOCTL_BATTERY_QUERY_TAG, new byte[4], 4);
                    if (tagBuf == null) return r;
                    uint tag = BitConverter.ToUInt32(tagBuf, 0);
                    if (tag == 0) return r;
                    r.Present = true;

                    // BATTERY_INFORMATION: Capabilities(0) Technology(4) Reserved[3] Chemistry[4](8) Designed(12) Full(16) Alert1 Alert2 Bias CycleCount(32)
                    var info = Query(h, tag, BatteryInformation, 36);
                    if (info != null)
                    {
                        bool relative = (BitConverter.ToUInt32(info, 0) & 0x40000000) != 0;   // BATTERY_CAPACITY_RELATIVE: not mWh
                        if (!relative)
                        {
                            r.DesignMWh = (int)BitConverter.ToUInt32(info, 12);
                            r.FullMWh = (int)BitConverter.ToUInt32(info, 16);
                        }
                        r.CycleCount = (int)BitConverter.ToUInt32(info, 32);
                    }

                    // BATTERY_WAIT_STATUS in -> BATTERY_STATUS out: PowerState, Capacity, Voltage, Rate
                    var ws = new byte[20];
                    BitConverter.GetBytes(tag).CopyTo(ws, 0);
                    var st = Ioctl(h, IOCTL_BATTERY_QUERY_STATUS, ws, 16);
                    if (st != null)
                    {
                        uint state = BitConverter.ToUInt32(st, 0);
                        r.Online = (state & 1) != 0;
                        r.Discharging = (state & 2) != 0;
                        r.Charging = (state & 4) != 0;
                        uint cap = BitConverter.ToUInt32(st, 4), mv = BitConverter.ToUInt32(st, 8);
                        int rate = BitConverter.ToInt32(st, 12);
                        if (cap != UNKNOWN) r.NowMWh = (int)cap;
                        if (mv != UNKNOWN) r.VoltageMV = (int)mv;
                        if (rate != RATE_UNKNOWN && rate != 0) { r.RateMW = rate; r.RateSource = L.S("电池驱动", "Battery driver"); }
                    }

                    var est = Query(h, tag, BatteryEstimatedTime, 4);
                    if (est != null && r.Discharging)
                    {
                        uint sec = BitConverter.ToUInt32(est, 0);
                        if (sec != UNKNOWN && sec > 0) r.RemainingMin = (int)(sec / 60);   // Windows' own estimate
                    }
                }
            }
            catch (Exception e) { App.Log(e); }

            if (knownRateMW.HasValue && r.Present) { r.RateMW = knownRateMW.Value; r.RateSource = L.S("EC 实时", "Live from the EC"); }
            if (adjust != null && r.Present) adjust(r);
            Estimate(r);
            return r;
        }

        // Rate source, in order: a live rate (EC / driver) > a regression over the last ~20 s – 3 min of capacity
        // readings. Rates are smoothed so the times don't jump; Windows' own estimate is kept when nothing better exists.
        static readonly object samplesLock = new object();   // Read() runs on the poll thread and the details loader

        static void Estimate(BatteryReading r)
        {
            if (r.NowMWh < 0) return;
            lock (samplesLock) EstimateLocked(r);
        }

        static void EstimateLocked(BatteryReading r)
        {
            var now = DateTime.Now;
            if (r.Charging != lastCharging) { samples.Clear(); emaRateMW = 0; }
            lastCharging = r.Charging;
            samples.Add(new KeyValuePair<DateTime, int>(now, r.NowMWh));
            samples.RemoveAll(p => (now - p.Key).TotalMinutes > 3);

            bool live = r.RateMW != 0;
            if (!live && samples.Count >= 3 && (now - samples[0].Key).TotalSeconds >= 20 && samples.Select(p => p.Value).Distinct().Count() >= 3)
            {
                // Least-squares slope of capacity over time, in mW (= mWh per hour).
                double t0 = samples[0].Key.Ticks, n = samples.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
                foreach (var p in samples)
                {
                    double x = (p.Key.Ticks - t0) / TimeSpan.TicksPerHour, y = p.Value;
                    sx += x; sy += y; sxx += x * x; sxy += x * y;
                }
                double den = n * sxx - sx * sx;
                if (den > 0) { r.RateMW = (int)Math.Round((n * sxy - sx * sy) / den); r.RateSource = L.S("按电量变化估算", "Estimated from charge change"); }
            }

            if (r.RateMW != 0)
            {
                emaRateMW = emaRateMW == 0 ? r.RateMW : emaRateMW * 0.7 + r.RateMW * 0.3;
                r.RateMW = (int)Math.Round(emaRateMW);
            }
            bool windowsEstimate = r.RemainingMin > 0;
            if (r.Discharging && r.RateMW < 0 && (live || !windowsEstimate))
                r.RemainingMin = (int)Math.Round(60.0 * r.NowMWh / -r.RateMW);
            if (r.Charging && r.RateMW > 0 && r.FullMWh > r.NowMWh)
                r.ToFullMin = (int)Math.Round(60.0 * (r.FullMWh - r.NowMWh) / r.RateMW);
        }
    }
}
