// Interface language: 简体中文 (zh-CN, the source text), 繁體中文 (zh-TW) and English (en).
// Code passes the Simplified text together with its English (and, where wording or jokes differ, a hand-written
// Traditional) version. Traditional text that isn't given is converted from the Simplified one by Windows
// (LCMapStringEx) and then fixed up: one-to-many characters (后/後, 里/裡 …) and Taiwan / Hong Kong wording
// (内存 → 記憶體, 屏幕 → 螢幕, 设置 → 設定 …).
using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HPControlPanel
{
    public static class L
    {
        public static string Lang = "zh-CN";   // zh-CN | zh-TW | en
        public static bool En { get { return Lang == "en"; } }
        public static bool Tw { get { return Lang == "zh-TW"; } }

        // Saved setting, or (when empty) the Windows display language.
        public static string Pick(string saved)
        {
            if (saved == "zh-CN" || saved == "zh-TW" || saved == "en") return saved;
            string ui = CultureInfo.CurrentUICulture.Name;
            if (Regex(ui, "^zh-(TW|HK|MO|Hant)")) return "zh-TW";
            if (ui.StartsWith("zh")) return "zh-CN";
            return "en";
        }

        static bool Regex(string s, string p) { return System.Text.RegularExpressions.Regex.IsMatch(s, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }

        public static string S(string cn, string en) { return En ? en : Tw ? ToTw(cn) : cn; }
        public static string S(string cn, string tw, string en) { return En ? en : Tw ? (tw ?? ToTw(cn)) : cn; }
        public static string F(string cn, string en, params object[] a) { return string.Format(S(cn, en), a); }
        public static string F3(string cn, string tw, string en, params object[] a) { return string.Format(S(cn, tw, en), a); }

        public static string Duration(int minutes)
        {
            int h = minutes / 60, m = minutes % 60;
            if (En) return h > 0 ? h + " h " + m + " min" : m + " min";
            return h > 0 ? h + S(" 小时 ", " 小時 ", "") + m + S(" 分", "") : m + S(" 分钟", "");
        }

        public static string FontFamily
        {
            get { return En ? "Segoe UI, Microsoft YaHei UI" : Tw ? "Microsoft JhengHei UI, Microsoft YaHei UI" : "Microsoft YaHei UI"; }
        }

        // ---------- Static text in MainWindow.xaml: Simplified → { Traditional (null = convert), English } ----------
        static readonly Dictionary<string, string[]> Xaml = new Dictionary<string, string[]>
        {
            { "HP 控制台", new[] { "HP 控制台", "HP Control Center" } },
            { "控制台", new[] { null, "Control Center" } },
            { "系统概览", new[] { null, "Overview" } },
            { "详细配置", new[] { "硬體規格", "Hardware" } },
            { "屏幕与声音", new[] { "螢幕與音效", "Display & Sound" } },
            { "电源与电池", new[] { null, "Power & Battery" } },
            { "快捷启动", new[] { null, "Quick Launch" } },
            { "设置", new[] { null, "Settings" } },
            { "系统操作", new[] { null, "System" } },
            { "锁屏", new[] { "鎖定", "Lock" } },
            { "睡眠", new[] { null, "Sleep" } },
            { "重启", new[] { "重新啟動", "Restart" } },
            { "关机", new[] { null, "Shut down" } },
            { "重启与关机需确认", new[] { "重新啟動與關機前會先確認", "Restart / shut down ask first" } },
            { "刷新数据", new[] { "重新整理", "Refresh" } },
            { "正在读取…", new[] { null, "Reading…" } },
            { "硬件监控", new[] { null, "Hardware monitor" } },
            { "传感器", new[] { null, "Sensor" } },
            { "当前值", new[] { "目前數值", "Value" } },
            { "状态 / 容量", new[] { null, "Status / capacity" } },
            { "曲线末端：—", new[] { "最新：—", "Latest: —" } },
            { "常用控制", new[] { null, "Quick controls" } },
            { "亮度", new[] { null, "Brightness" } },
            { "音量", new[] { null, "Volume" } },
            { "电源模式", new[] { null, "Power mode" } },
            { "省电", new[] { null, "Saver" } },
            { "平衡", new[] { null, "Balanced" } },
            { "高性能", new[] { null, "Performance" } },
            { "静音", new[] { null, "Mute" } },
            { "快捷开关", new[] { null, "Quick switches" } },
            { "HP 娘", new[] { "HP 娘", "HP-chan" } },
            { "电量 —", new[] { null, "Battery —" } },
            { "你好呀。", new[] { "你好呀～", "Hi there!" } },
            { "天气：获取中…", new[] { null, "Weather: loading…" } },
            { "详细信息", new[] { "詳細資訊", "Details" } },
            { "复制全部", new[] { null, "Copy all" } },
            { "正在读取硬件信息…", new[] { null, "Reading hardware info…" } },
            { "屏幕亮度", new[] { null, "Screen brightness" } },
            { "调节笔记本内置屏幕的亮度（外接显示器不支持）。", new[] { "調整筆電內建螢幕的亮度（不支援外接螢幕）。", "Adjusts the built-in screen (external monitors aren't supported)." } },
            { "调节默认播放设备的主音量。", new[] { "調整預設播放裝置的主音量。", "Adjusts the master volume of the default playback device." } },
            { "和 Windows 电池图标里的“电源模式”是同一个设置。", new[] { "和 Windows 電池圖示裡的「電源模式」是同一個設定。", "Same setting as “Power mode” in the Windows battery flyout." } },
            { "散热信息（只读）", new[] { null, "Thermals (read-only)" } },
            { "电池", new[] { null, "Battery" } },
            { "电源计划", new[] { null, "Power plan" } },
            { "自定义…", new[] { "自訂…", "Customize…" } },
            { "控制面板里的电源计划。“自定义”会打开 Windows 电源选项，可以新建或修改计划。",
                new[] { "Windows 控制台的電源計畫。「自訂」會開啟 Windows 電源選項，可以新增或修改計畫。",
                        "Power plans from Control Panel. “Customize” opens Windows Power Options to create or edit plans." } },
            { "GitHub 主页", new[] { "GitHub 頁面", "GitHub page" } },
            { "程序列表", new[] { null, "Programs" } },
            { "+  添加程序", new[] { "+  新增程式", "+  Add program" } },
            { "打开或关闭常用程序。关闭写字板、记事本时会提示保存未保存的内容。自己添加的程序可以移除。",
                new[] { "開啟或關閉常用程式。關閉 WordPad、記事本時會提示儲存尚未儲存的內容。自己新增的程式可以移除。",
                        "Open or close common programs. WordPad and Notepad ask to save unsaved work before closing. Programs you added can be removed." } },
            { "人物图片", new[] { "角色圖片", "Character image" } },
            { "默认：HP 娘", new[] { null, "Default: HP-chan" } },
            { "选择图片…", new[] { null, "Choose…" } },
            { "恢复默认", new[] { "還原預設", "Reset" } },
            { "主题色", new[] { "主題色", "Theme color" } },
            { "蓝色", new[] { null, "Blue" } },
            { "粉色", new[] { null, "Pink" } },
            { "紫色", new[] { null, "Purple" } },
            { "刷新间隔", new[] { "更新頻率", "Refresh interval" } },
            { "监控数据多久更新一次", new[] { "監控資料多久更新一次", "How often readings update" } },
            { "1 秒", new[] { null, "1 s" } },
            { "2 秒", new[] { null, "2 s" } },
            { "5 秒", new[] { null, "5 s" } },
            { "温度提醒", new[] { null, "Temperature alert" } },
            { "达到这个温度时 HP 娘会提醒你", new[] { null, "HP-chan warns you at this temperature" } },
            { "开机自动启动", new[] { "開機自動啟動", "Start with Windows" } },
            { "登录后自动打开，不弹管理员确认", new[] { "登入後自動開啟，不會跳出管理員確認", "Opens after sign-in, no admin prompt" } },
            { "自动检查更新", new[] { null, "Check for updates automatically" } },
            { "每天最多查一次，有新版本只在这里提示", new[] { "每天最多檢查一次，有新版本只在這裡提示", "At most once a day; new versions are only shown here" } },
            { "窗口置顶", new[] { "視窗置頂", "Always on top" } },
            { "让控制台始终显示在其他窗口上面", new[] { "讓控制台永遠顯示在其他視窗上面", "Keep this window above other windows" } },
            { "语言", new[] { "語言", "Language" } },
            { "切换后控制台会自动重启", new[] { "切換後控制台會自動重新啟動", "The app restarts to switch" } },
            { "简体中文", new[] { "简体中文", "简体中文" } },
            { "繁體中文", new[] { "繁體中文", "繁體中文" } },
            { "天气位置", new[] { null, "Weather location" } },
            { "Windows 定位", new[] { "Windows 定位", "Windows location" } },
            { "按 IP", new[] { "依 IP", "By IP" } },
            { "手动选择", new[] { null, "Pick a city" } },
            { "输入城市或地区名，如 东京、新宿、Tai Po", new[] { "輸入城市或地區名稱，例如 東京、新宿、大埔", "Type a city or area, e.g. Tokyo, Shinjuku, Tai Po" } },
            { "搜索", new[] { null, "Search" } },
            { "当前：—", new[] { "目前：—", "Now: —" } },
            { "关于", new[] { null, "About" } },
            { "当前版本", new[] { "目前版本", "Version" } },
            { "检查更新", new[] { null, "Check updates" } },
        };

        public static string T(string cn)
        {
            if (string.IsNullOrEmpty(cn) || Lang == "zh-CN") return cn;
            string[] e;
            if (Xaml.TryGetValue(cn, out e)) return En ? e[1] : (e[0] ?? ToTw(cn));
            return En ? cn : ToTw(cn);
        }

        // Applies T() to every static text in the loaded window.
        public static void Localize(DependencyObject o)
        {
            if (Lang == "zh-CN") return;
            var w = o as Window;
            if (w != null) w.Title = T(w.Title);
            var tb = o as TextBlock;
            if (tb != null && !string.IsNullOrEmpty(tb.Text)) tb.Text = T(tb.Text);
            var cc = o as ContentControl;
            if (cc != null && cc.Content is string) cc.Content = T((string)cc.Content);
            var fe = o as FrameworkElement;
            if (fe != null && fe.ToolTip is string) fe.ToolTip = T((string)fe.ToolTip);
            foreach (var c in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().ToList()) Localize(c);
        }

        // ---------- Simplified → Traditional ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string locale, uint flags, string src, int cchSrc, [Out] char[] dest, int cchDest, IntPtr ver, IntPtr reserved, IntPtr sortHandle);
        const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

        // Applied in order after the character conversion. Longer phrases come before the words they contain.
        static readonly string[][] TwFixes =
        {
            // characters Windows maps one-to-one where Traditional has several
            new[] { "后", "後" }, new[] { "里", "裡" }, new[] { "么", "麼" }, new[] { "于", "於" }, new[] { "臺", "台" },
            new[] { "云", "雲" }, new[] { "范", "範" }, new[] { "占", "佔" }, new[] { "采", "採" }, new[] { "游戲", "遊戲" },
            new[] { "復制", "複製" }, new[] { "制作", "製作" }, new[] { "自制", "自製" }, new[] { "重復", "重複" }, new[] { "關系", "關係" },
            new[] { "干凈", "乾淨" }, new[] { "干嘛", "幹嘛" }, new[] { "干活", "幹活" }, new[] { "能干", "能幹" }, new[] { "頭發", "頭髮" },
            new[] { "放松", "放鬆" }, new[] { "輕松", "輕鬆" }, new[] { "奮斗", "奮鬥" }, new[] { "周末", "週末" },
            // Taiwan / Hong Kong wording
            new[] { "文件資源管理器", "檔案總管" }, new[] { "文件管理", "檔案總管" }, new[] { "文件", "檔案" },
            new[] { "筆記本電腦", "筆電" }, new[] { "筆記本", "筆電" }, new[] { "寫字板", "WordPad" }, new[] { "計算器", "小算盤" },
            new[] { "固態硬盤", "固態硬碟" }, new[] { "機械硬盤", "傳統硬碟" }, new[] { "硬盤", "硬碟" }, new[] { "磁盤", "磁碟" },
            new[] { "集成顯卡", "內顯" }, new[] { "核顯", "內顯" }, new[] { "顯卡", "顯示卡" }, new[] { "顯示器", "螢幕" }, new[] { "屏幕", "螢幕" },
            new[] { "內存", "記憶體" }, new[] { "硬件", "硬體" }, new[] { "軟件", "軟體" }, new[] { "傳感器", "感測器" },
            new[] { "主板", "主機板" }, new[] { "聲卡", "音效卡" }, new[] { "網卡", "網路卡" }, new[] { "網絡", "網路" }, new[] { "攝像頭", "相機" },
            new[] { "設置", "設定" }, new[] { "默認", "預設" }, new[] { "程序", "程式" }, new[] { "信息", "資訊" }, new[] { "數據", "資料" },
            new[] { "窗口", "視窗" }, new[] { "視頻", "影片" }, new[] { "刷新", "重新整理" }, new[] { "保存", "儲存" }, new[] { "運行", "執行" },
            new[] { "快捷方式", "捷徑" }, new[] { "圖標", "圖示" }, new[] { "高性能", "高效能" }, new[] { "性能", "效能" }, new[] { "分辨率", "解析度" },
            new[] { "英寸", "吋" }, new[] { "線程", "執行緒" }, new[] { "剪貼板", "剪貼簿" }, new[] { "搜索", "搜尋" }, new[] { "只讀", "唯讀" },
            new[] { "登錄", "登入" }, new[] { "支持", "支援" }, new[] { "適配", "支援" }, new[] { "質量", "品質" }, new[] { "內置", "內建" },
            new[] { "加載", "載入" }, new[] { "快捷啟動", "快速啟動" }, new[] { "快捷開關", "快速開關" }, new[] { "添加", "新增" },
            new[] { "打開", "開啟" }, new[] { "當前", "目前" }, new[] { "檢測", "偵測" }, new[] { "電源適配器", "變壓器" },
            new[] { "通過", "透過" }, new[] { "示例", "範例" },
        };

        static readonly Dictionary<string, string> twCache = new Dictionary<string, string>();

        public static string ToTw(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            lock (twCache)
            {
                string r;
                if (twCache.TryGetValue(s, out r)) return r;
                var buf = new char[s.Length * 2 + 16];
                int n = LCMapStringEx("zh-CN", LCMAP_TRADITIONAL_CHINESE, s, s.Length, buf, buf.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                r = n > 0 ? new string(buf, 0, n) : s;
                foreach (var f in TwFixes) r = r.Replace(f[0], f[1]);
                twCache[s] = r;
                return r;
            }
        }
    }
}
