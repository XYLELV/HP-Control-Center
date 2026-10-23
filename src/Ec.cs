// Read-only access to the embedded controller through PawnIO + LpcACPIEC, using the board's EcProfile.
// Only the ACPI EC "read" command (0x80) is ever sent. There is deliberately no EC write here.
using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace HPControlPanel
{
    public class EcSnapshot
    {
        public int FanRpm = -1;
        public bool FanFault;
        public int CpuTemp = -1;
        public int BoardTemp = -1;
        public int GpuTemp = -1;       // discrete GPU sensor; 0 when absent
        public int PassiveTemp = -1;   // throttle point
        public int CriticalTemp = -1;  // forced shutdown point
        // Battery straight from the smart battery (only on boards whose profile maps it)
        public int BatRateMA = int.MinValue;     // signed: negative while discharging
        public int BatVoltMV = -1, BatDesignMAh = -1, BatFullMAh = -1, BatDesignMV = -1, BatCycles = -1;

        public EcSnapshot Copy() { return (EcSnapshot)MemberwiseClone(); }
    }

    public sealed class Ec : IDisposable
    {
        // Resolved at runtime (LoadLibrary below) so no install path is baked into the exe.
        const string Lib = "PawnIOLib.dll";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport(Lib)] static extern int pawnio_open(out IntPtr handle);
        [DllImport(Lib)] static extern int pawnio_load(IntPtr handle, byte[] blob, UIntPtr size);
        [DllImport(Lib)] static extern int pawnio_close(IntPtr handle);
        [DllImport(Lib, CharSet = CharSet.Ansi)]
        static extern int pawnio_execute(IntPtr handle, string name, ulong[] input, UIntPtr inSize, ulong[] output, UIntPtr outSize, out UIntPtr returnSize);

        const byte EC_DATA = 0x62, EC_CMD = 0x66;
        const byte EC_READ = 0x80;
        const byte OBF = 0x01, IBF = 0x02;

        IntPtr h;
        Mutex mtx;
        EcProfile profile;
        public string Error { get; private set; }

        public bool Open(EcProfile p, string modulePath)
        {
            profile = p;
            if (p == null) { Error = L.S("此机型的风扇读取尚未适配", "此機型尚未支援風扇讀取", "fan reading isn't supported on this model"); return false; }
            try
            {
                string lib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", Lib);
                if (!File.Exists(lib) || LoadLibrary(lib) == IntPtr.Zero) { Error = L.S("没有安装 PawnIO 驱动", "沒有安裝 PawnIO 驅動程式", "the PawnIO driver isn't installed"); return false; }
                if (!File.Exists(modulePath)) { Error = L.S("找不到 LpcACPIEC.bin", "LpcACPIEC.bin is missing"); return false; }
                int hr = pawnio_open(out h);
                if (hr < 0) { Error = L.S("PawnIO 打开失败 0x", "PawnIO 開啟失敗 0x", "PawnIO failed to open 0x") + hr.ToString("X8"); return false; }
                byte[] blob = File.ReadAllBytes(modulePath);
                hr = pawnio_load(h, blob, (UIntPtr)blob.Length);
                if (hr < 0) { Error = L.S("EC 模块加载失败 0x", "EC module failed to load 0x") + hr.ToString("X8"); return false; }
                mtx = new Mutex(false, @"Global\Access_EC");
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        byte Inb(byte port)
        {
            var o = new ulong[1]; UIntPtr n;
            if (pawnio_execute(h, "ioctl_pio_read", new ulong[] { port }, (UIntPtr)1, o, (UIntPtr)1, out n) < 0) throw new IOException("pio_read");
            return (byte)o[0];
        }

        void Outb(byte port, byte val)
        {
            UIntPtr n;
            if (pawnio_execute(h, "ioctl_pio_write", new ulong[] { port, val }, (UIntPtr)2, new ulong[1], (UIntPtr)0, out n) < 0) throw new IOException("pio_write");
        }

        bool Wait(Func<byte, bool> ok)
        {
            for (int i = 0; i < 2000; i++) { if (ok(Inb(EC_CMD))) return true; Thread.SpinWait(200); }
            return false;
        }

        int ReadReg(byte addr)
        {
            if (!Wait(s => (s & IBF) == 0)) return -1;
            // A pending output byte belongs to someone else (usually Windows' own ACPI EC driver):
            // wait for its owner to collect it instead of reading it away, and give up if it doesn't.
            if (!Wait(s => (s & OBF) == 0)) return -1;
            Outb(EC_CMD, EC_READ);
            if (!Wait(s => (s & IBF) == 0)) return -1;
            Outb(EC_DATA, addr);
            if (!Wait(s => (s & OBF) != 0)) return -1;
            return Inb(EC_DATA);
        }

        // Two matching reads in a row, to filter collisions with the Windows ACPI EC driver.
        int Stable(byte addr)
        {
            for (int t = 0; t < 5; t++)
            {
                int a = ReadReg(addr), b = ReadReg(addr);
                if (a >= 0 && a == b) return a;
            }
            return -1;
        }

        // 16-bit value split over two registers: re-read the high byte and retry if it moved in between,
        // otherwise e.g. 0x08FF -> 0x0900 could be read as 0x09FF.
        int ReadWord(byte lo, byte hi)
        {
            for (int t = 0; t < 3; t++)
            {
                int h1 = Stable(hi), l = Stable(lo), h2 = Stable(hi);
                if (h1 >= 0 && l >= 0 && h1 == h2) return l | (h1 << 8);
            }
            return -1;
        }

        EcSnapshot lastGood = new EcSnapshot();
        int passive = -1, critical = -1;   // trip points are fixed; read once
        int batDesign = -1, batFull = -1, batDesignV = -1, batCycles = -1;   // change rarely; read once per run

        public EcSnapshot Read()
        {
            if (h == IntPtr.Zero || profile == null) return new EcSnapshot();
            var p = profile;
            bool got;
            try { got = mtx.WaitOne(500); } catch (AbandonedMutexException) { got = true; }
            // Another tool (FanControl, LibreHardwareMonitor...) holds the EC: don't interleave with it.
            // A copy, because callers fill in fallbacks (e.g. the ACPI temperature) on what they get.
            if (!got) return lastGood.Copy();
            var s = new EcSnapshot();
            try
            {
                s.FanRpm = ReadWord(p.FanRpmLo, p.FanRpmHi);
                int f = Stable(p.FanFault);
                s.FanFault = f >= 0 && ((f >> p.FanFaultBit) & 1) != 0;
                s.CpuTemp = Stable(p.CpuTemp);
                s.BoardTemp = Stable(p.BoardTemp);
                s.GpuTemp = Stable(p.GpuTemp);
                if (passive <= 0) passive = Stable(p.PassiveTemp);
                if (critical <= 0) critical = Stable(p.CriticalTemp);
                s.PassiveTemp = passive;
                s.CriticalTemp = critical;
                if (p.BatRate != 0)
                {
                    int rate = ReadWord(p.BatRate, (byte)(p.BatRate + 1));
                    if (rate >= 0) s.BatRateMA = (short)rate;   // two's complement
                    s.BatVoltMV = ReadWord(p.BatVolt, (byte)(p.BatVolt + 1));
                    if (batDesign <= 0) batDesign = ReadWord(p.BatDesign, (byte)(p.BatDesign + 1));
                    if (batFull <= 0) batFull = ReadWord(p.BatFull, (byte)(p.BatFull + 1));
                    if (batDesignV <= 0) batDesignV = ReadWord(p.BatDesignVolt, (byte)(p.BatDesignVolt + 1));
                    if (batCycles < 0) batCycles = Stable(p.BatCycles);
                    s.BatDesignMAh = batDesign; s.BatFullMAh = batFull; s.BatDesignMV = batDesignV; s.BatCycles = batCycles;
                }
                if (s.CpuTemp >= 0) lastGood = s;
            }
            catch (Exception e) { Error = e.Message; }
            finally { mtx.ReleaseMutex(); }
            return s;
        }

        public void Dispose()
        {
            if (h != IntPtr.Zero) { pawnio_close(h); h = IntPtr.Zero; }
            if (mtx != null) mtx.Dispose();
        }
    }
}
