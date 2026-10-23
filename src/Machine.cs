// Identifies the computer this runs on (nothing here is specific to one machine).
using System;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.Win32;

namespace HPControlPanel
{
    public class Machine
    {
        public string Maker = "", Product = "", Board = "", Bios = "";
        public string Cpu = "", Gpu = "";
        public int RamGb;

        // "HP 240 G8 Notebook PC" -> "HP 240 G8"
        public string ShortModel
        {
            get
            {
                string m = Regex.Replace(Product, @"\s+(Notebook|Laptop|Desktop)(\s+PC)?$", "", RegexOptions.IgnoreCase).Trim();
                return m.Length > 0 ? m : L.S("这台电脑", "This PC");
            }
        }

        public static Machine Detect()
        {
            var m = new Machine();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    m.Maker = Str(k, "SystemManufacturer");
                    m.Product = Str(k, "SystemProductName");
                    m.Board = Str(k, "BaseBoardProduct");
                    m.Bios = Str(k, "BIOSVersion");
                }
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    m.Cpu = CleanCpu(Str(k, "ProcessorNameString"));
            }
            catch { }

            try
            {
                var names = new List<string>();
                using (var q = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject mo in q.Get())
                    {
                        string n = Clean(Convert.ToString(mo["Name"]));
                        if (n.Length > 0 && !n.StartsWith("Microsoft") && !names.Contains(n)) names.Add(n);
                    }
                m.Gpu = string.Join(" + ", names);
            }
            catch { }

            double used, total;
            SystemInfo.Memory(out used, out total);
            m.RamGb = (int)Math.Round(total);
            return m;
        }

        static string Str(RegistryKey k, string name)
        {
            return k == null ? "" : (Convert.ToString(k.GetValue(name)) ?? "").Trim();
        }

        // "Intel(R) Core(TM)  i3" -> "Intel Core i3" (also used by the details page)
        public static string Clean(string s)
        {
            return Regex.Replace((s ?? "").Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", ""), @"\s+", " ").Trim();
        }

        // "11th Gen Intel(R) Core(TM) i3-1125G4 @ 2.00GHz" -> "Intel Core i3-1125G4"
        // "AMD Ryzen 5 5500U with Radeon Graphics" -> "AMD Ryzen 5 5500U"
        static string CleanCpu(string s)
        {
            s = Clean(s);
            s = Regex.Replace(s, @"\s*@.*$", "");
            s = Regex.Replace(s, @"^\d+(st|nd|rd|th) Gen\s+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+with\s+.*Graphics$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+(\d+-Core\s+)?Processor$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+CPU$", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }
    }

    // Where a motherboard's EC keeps the fan and temperature values.
    // Only boards whose map was verified are listed; every other machine falls back to ACPI temperature.
    public class EcProfile
    {
        public string Name;
        public byte FanRpmLo, FanRpmHi, FanFault, FanFaultBit, CpuTemp, BoardTemp, GpuTemp, PassiveTemp, CriticalTemp;
        // Smart-battery words (16-bit little-endian); 0 = not mapped on this board.
        public byte BatRate, BatVolt, BatDesign, BatFull, BatDesignVolt, BatCycles;

        public static EcProfile For(Machine m)
        {
            // HP 240 G8, board 8819: parsed from its DSDT (region H2RM) and checked against Windows' own TZ00 reading.
            if (m.Maker.StartsWith("HP", StringComparison.OrdinalIgnoreCase) && string.Equals(m.Board, "8819", StringComparison.OrdinalIgnoreCase))
                return new EcProfile
                {
                    Name = L.S("HP 主板 8819", "HP board 8819"),
                    FanRpmLo = 0x7E, FanRpmHi = 0x7F, FanFault = 0xF5, FanFaultBit = 0,
                    // VTMP 0xDD is the discrete-GPU sensor; it reads 0 on units with only integrated graphics.
                    CpuTemp = 0xDB, BoardTemp = 0xDC, GpuTemp = 0xDD, PassiveTemp = 0xF0, CriticalTemp = 0xF1,
                    // BPR0 signed mA (negative = discharging), BPV0 mV, BDC0/BFC0 mAh, BDV0 mV, BCC0 cycles —
                    // checked against Windows' remaining capacity / voltage and the driver's cycle count.
                    BatRate = 0x9B, BatVolt = 0xC1, BatDesign = 0x89, BatFull = 0x8D, BatDesignVolt = 0x93, BatCycles = 0xA3
                };
            return null;
        }
    }
}
