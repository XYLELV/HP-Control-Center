// Hardware summary for the "详细配置" page, in the style of 图吧工具箱's 详细信息.
// Everything comes from WMI / the registry of the machine it runs on.
using System;
using System.Linq;
using System.Management;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HPControlPanel
{
    public static class SystemDetails
    {
        // label -> one or more lines
        public static List<KeyValuePair<string, List<string>>> Collect(Machine m, BatteryReading bat)
        {
            var rows = new List<KeyValuePair<string, List<string>>>();
            Action<string, IEnumerable<string>> add = (label, lines) =>
            {
                var l = lines.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                rows.Add(new KeyValuePair<string, List<string>>(label, l.Count > 0 ? l : new List<string> { "—" }));
            };

            add(L.S("处理器", "Processor"), Safe(() => Cpu(m)));
            add(L.S("主板", "Motherboard"), Safe(() => Board(m)));
            add("BIOS", Safe(() => Bios()));
            add(L.S("内存", "Memory"), Safe(() => Memory()));
            add(L.S("显卡", "Graphics"), Safe(() => Graphics()));
            add(L.S("显示器", "Display"), Safe(() => Monitors()));
            add(L.S("磁盘", "Storage"), Safe(() => Disks()));
            add(L.S("声卡", "Audio"), Safe(() => Query("SELECT Name FROM Win32_SoundDevice").Select(o => Str(o["Name"])).Distinct()));
            add(L.S("网卡", "Network"), Safe(() => Network()));
            add(L.S("电池", "Battery"), new[] { bat != null && bat.Present && bat.DesignMWh > 0
                ? L.F("设计 {0} Wh · 健康度 {1}% · 循环 {2} 次", "Design {0} Wh · health {1}% · {2} cycles", (bat.DesignMWh / 1000.0).ToString("F2"), Math.Round(bat.HealthPercent), bat.CycleCount)
                : bat != null && bat.Present ? L.S("已检测到电池", "Battery present") : L.S("没有电池", "No battery") });
            add(L.S("系统", "System"), Safe(() => Os()));
            return rows;
        }

        static string Paren(string s) { return L.S("（", " (") + s + L.S("）", ")"); }

        static IEnumerable<string> Safe(Func<IEnumerable<string>> f)
        {
            try { return f().ToList(); } catch (Exception e) { App.Log(e); return new string[0]; }
        }

        static List<ManagementBaseObject> Query(string wql, string scope = null)
        {
            using (var q = scope == null ? new ManagementObjectSearcher(wql) : new ManagementObjectSearcher(scope, wql))
                return q.Get().Cast<ManagementBaseObject>().ToList();
        }

        static string Str(object o) { return Convert.ToString(o ?? "").Trim(); }

        // Vendor names as Chinese users know them (Simplified, Traditional); English keeps the original name.
        static string Vendor(string v)
        {
            if (L.En) return v;
            var map = new[] {
                new[] { @"^(HP|Hewlett)", "惠普", "惠普" }, new[] { "^Lenovo", "联想", "聯想" }, new[] { "^Dell", "戴尔", "戴爾" }, new[] { "^ASUS", "华硕", "華碩" },
                new[] { "^Acer", "宏碁", "宏碁" }, new[] { "^(MSI|Micro-Star)", "微星", "微星" }, new[] { "^Huawei", "华为", "華為" }, new[] { "^Xiaomi", "小米", "小米" },
                new[] { "^Gigabyte", "技嘉", "技嘉" }, new[] { "^Samsung", "三星", "三星" }, new[] { "^Intel", "英特尔", "英特爾" }, new[] { "^Micron", "美光", "美光" },
                new[] { "^(SK ?Hynix|Hynix)", "海力士", "海力士" }, new[] { "^Kingston", "金士顿", "金士頓" }, new[] { "^Realtek", "瑞昱", "瑞昱" }, new[] { "^NVIDIA", "英伟达", "輝達" },
                new[] { "^(Advanced Micro Devices|AMD)", "AMD", "AMD" }, new[] { "^Crucial", "英睿达", "美光 Crucial" }, new[] { "^ADATA", "威刚", "威剛" },
            };
            foreach (var p in map) if (Regex.IsMatch(v, p[0], RegexOptions.IgnoreCase)) return L.Tw ? p[2] : p[1];
            return v;
        }

        static IEnumerable<string> Cpu(Machine m)
        {
            var p = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").First();
            int cores = Convert.ToInt32(p["NumberOfCores"]), threads = Convert.ToInt32(p["NumberOfLogicalProcessors"]);
            var ghz = Regex.Match(Str(p["Name"]), @"@\s*([\d.]+)\s*GHz");
            string clock = ghz.Success ? ghz.Groups[1].Value + " GHz" : (Convert.ToInt32(p["MaxClockSpeed"]) / 1000.0).ToString("F2") + " GHz";
            string[] cn = { "", "单核", "双核", "三核", "四核", "五核", "六核", "七核", "八核" };
            string coreText = L.En ? cores + (cores == 1 ? " core" : " cores") : cores < cn.Length ? L.S(cn[cores], "") : cores + L.S(" 核", "");
            yield return m.Cpu + " @ " + clock + "  " + coreText + " / " + threads + L.S(" 线程", " 執行緒", " threads");
        }

        static IEnumerable<string> Board(Machine m)
        {
            var b = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").First();
            string pc = m.Product.Length > 0 ? Paren(m.Product) : "";
            yield return Vendor(Str(b["Manufacturer"])) + " " + Str(b["Product"]) + pc;
        }

        static IEnumerable<string> Bios()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            {
                string date = Str(k.GetValue("BIOSReleaseDate"));
                DateTime d;
                if (DateTime.TryParseExact(date, "MM/dd/yyyy", null, System.Globalization.DateTimeStyles.None, out d)) date = d.ToString("yyyy/MM/dd");
                yield return Str(k.GetValue("BIOSVendor")) + " " + Str(k.GetValue("BIOSVersion")) + (date.Length > 0 ? Paren(date) : "");
            }
        }

        static IEnumerable<string> Memory()
        {
            var sticks = Query("SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            if (sticks.Count == 0) yield break;
            var sizes = sticks.Select(s => (int)Math.Round(Convert.ToDouble(s["Capacity"]) / 1073741824.0)).ToList();
            int type = Convert.ToInt32(sticks[0]["SMBIOSMemoryType"] ?? 0);
            string ddr = type == 34 ? "DDR5" : type == 35 ? "LPDDR5" : type == 26 ? "DDR4" : type == 30 ? "LPDDR4" : type == 24 ? "DDR3" : type == 29 ? "LPDDR3" : "";
            int mhz = sticks.Select(s => Convert.ToInt32(s["ConfiguredClockSpeed"] ?? s["Speed"] ?? 0)).Max();
            yield return sizes.Sum() + " GB " + ddr + (mhz > 0 ? " " + mhz + " MHz" : "") + Paren(string.Join(" + ", sizes.Select(x => x + " GB")));
            yield return string.Join(" · ", sticks.Select(s => (Vendor(Str(s["Manufacturer"])) + " " + Str(s["PartNumber"])).Trim()));
        }

        static IEnumerable<string> Graphics()
        {
            foreach (var g in Query("SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController"))
            {
                string name = Machine.Clean(Str(g["Name"]));
                if (name.StartsWith("Microsoft")) continue;
                // AdapterRAM is 32-bit (wrong for cards with 4 GB or more); prefer the driver's own 64-bit size.
                double gb = DriverVramGb(Str(g["PNPDeviceID"]));
                if (gb <= 0) gb = Convert.ToDouble(g["AdapterRAM"] ?? 0) / 1073741824.0;
                var sub = Regex.Match(Str(g["PNPDeviceID"]), @"SUBSYS_[0-9A-F]{4}([0-9A-F]{4})", RegexOptions.IgnoreCase);
                string maker = sub.Success ? PciVendor(sub.Groups[1].Value) : "";
                var parts = new List<string>();
                if (gb >= 0.1) parts.Add(gb >= 1 ? Math.Round(gb) + " GB" : Math.Round(gb * 1024) + " MB");
                if (maker.Length > 0) parts.Add(maker);
                yield return name + (parts.Count > 0 ? Paren(string.Join(" / ", parts)) : "");
            }
        }

        // HardwareInformation.qwMemorySize from the display-class key whose MatchingDeviceId fits this adapter.
        static double DriverVramGb(string pnpId)
        {
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        using (var k = cls.OpenSubKey(sub))
                        {
                            string match = k == null ? "" : Str(k.GetValue("MatchingDeviceId"));
                            if (match.Length == 0 || !pnpId.StartsWith(match, StringComparison.OrdinalIgnoreCase)) continue;
                            double gb = Gpu.DriverMemoryGb(k);
                            if (gb > 0) return gb;
                        }
                    }
            }
            catch { }
            return 0;
        }

        static string PciVendor(string id)
        {
            switch (id.ToUpperInvariant())
            {
                case "103C": return Vendor("HP"); case "17AA": return Vendor("Lenovo"); case "1028": return Vendor("Dell"); case "1043": return Vendor("ASUS");
                case "1025": return Vendor("Acer"); case "1462": return Vendor("MSI"); case "19E5": return Vendor("Huawei"); case "1458": return Vendor("Gigabyte");
                case "144D": return Vendor("Samsung"); case "8086": return Vendor("Intel"); case "10DE": return Vendor("NVIDIA"); case "1002": return "AMD";
                default: return "";
            }
        }

        static IEnumerable<string> Monitors()
        {
            var ids = Query("SELECT InstanceName, ManufacturerName, ProductCodeID, UserFriendlyName FROM WmiMonitorID", @"root\wmi");
            var sizes = Query("SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", @"root\wmi");
            var res = Query("SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController")
                .FirstOrDefault(v => v["CurrentHorizontalResolution"] != null);
            foreach (var mon in ids)
            {
                string maker = Edid(mon["ManufacturerName"]), code = Edid(mon["ProductCodeID"]), name = Edid(mon["UserFriendlyName"]);
                var size = sizes.FirstOrDefault(s => Str(s["InstanceName"]) == Str(mon["InstanceName"]));
                var extra = new List<string>();
                if (size != null)
                {
                    double w = Convert.ToDouble(size["MaxHorizontalImageSize"]), h = Convert.ToDouble(size["MaxVerticalImageSize"]);
                    if (w > 0 && h > 0) extra.Add((Math.Sqrt(w * w + h * h) / 2.54).ToString("F1") + L.S(" 英寸", " 吋", "\""));
                }
                if (res != null && ids.Count == 1)
                    extra.Add(res["CurrentHorizontalResolution"] + "×" + res["CurrentVerticalResolution"] + " " + res["CurrentRefreshRate"] + "Hz");
                string panel = PanelMaker(maker);
                yield return ((panel.Length > 0 ? panel + " " : "") + maker + code + (name.Length > 0 ? " " + name : "")).Trim()
                    + (extra.Count > 0 ? Paren(string.Join(L.S("，", ", "), extra)) : "");
            }
        }

        static string Edid(object arr)
        {
            var a = arr as ushort[];
            return a == null ? "" : new string(a.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
        }

        static string PanelMaker(string code)
        {
            if (L.En) return "";   // the EDID code (BOE, AUO …) is shown anyway
            switch (code)
            {
                case "BOE": return L.S("京东方", ""); case "AUO": return L.S("友达", ""); case "LGD": return "LG"; case "SDC": return "三星";
                case "CMN": return L.S("群创", ""); case "IVO": return L.S("龙腾", ""); case "SHP": return "夏普"; case "CSO": case "CSW": return L.S("华星光电", "");
                case "TMA": return L.S("天马", "");
                default: return "";
            }
        }

        static IEnumerable<string> Disks()
        {
            var media = new Dictionary<string, int>();
            try
            {
                foreach (var d in Query("SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
                    media[Str(d["FriendlyName"])] = Convert.ToInt32(d["MediaType"] ?? 0);
            }
            catch { }
            foreach (var d in Query("SELECT Model, Size FROM Win32_DiskDrive"))
            {
                string model = Str(d["Model"]);
                double gb = Convert.ToDouble(d["Size"] ?? 0) / 1e9;   // decimal GB, as printed on the box
                int mt; media.TryGetValue(model, out mt);
                string kind = mt == 4 ? L.S("固态硬盘", "SSD") : mt == 3 ? L.S("机械硬盘", "HDD") : "";
                string size = gb >= 1000 ? (gb / 1000).ToString("0.#") + " TB" : Math.Round(gb) + " GB";
                yield return model + Paren(size + (kind.Length > 0 ? L.S("，", ", ") + kind : ""));
            }
        }

        // Physical PCI/USB adapters only (no VPN, Bluetooth PAN or other virtual adapters).
        static IEnumerable<string> Network()
        {
            return Query("SELECT Name, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True")
                .Where(n => Regex.IsMatch(Str(n["PNPDeviceID"]), @"^(PCI|USB)\\", RegexOptions.IgnoreCase))
                .Select(n => Machine.Clean(Str(n["Name"]))).Distinct();
        }

        static IEnumerable<string> Os()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int build; int.TryParse(Str(k.GetValue("CurrentBuild")), out build);
                string ver = build >= 22000 ? "Windows 11" : "Windows 10";   // ProductName says "Windows 10" on 11 too
                string ed = Str(k.GetValue("EditionID"));
                string edition = ed == "Core" ? L.S("家庭版", "家用版", "Home") : ed == "CoreCountrySpecific" ? L.S("家庭中文版", "家用中文版", "Home China") : ed == "CoreSingleLanguage" ? L.S("家庭单语言版", "家用單一語言版", "Home Single Language")
                    : ed == "Professional" ? L.S("专业版", "Pro") : ed == "Enterprise" ? L.S("企业版", "Enterprise") : ed == "Education" ? L.S("教育版", "Education") : ed;
                yield return ver + " " + edition + " " + Str(k.GetValue("DisplayVersion")) + Paren(build + "." + Str(k.GetValue("UBR")));
            }
        }

        public static string AsText(List<KeyValuePair<string, List<string>>> rows)
        {
            return string.Join(Environment.NewLine, rows.Select(r => r.Key + L.S("：", ": ") + string.Join(Environment.NewLine + L.S("　　　", "    "), r.Value)));
        }
    }
}
