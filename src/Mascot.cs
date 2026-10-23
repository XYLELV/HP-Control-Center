// What HP 娘 says, based on the latest readings. Each language has its own lines and memes
// (L.S / L.F pick the interface language; see I18n.cs).
using System;
using System.Linq;
using System.Collections.Generic;

namespace HPControlPanel
{
    public class Snapshot
    {
        public EcSnapshot Ec = new EcSnapshot();
        public bool EcOk;
        public bool TempFromAcpi;      // CPU temperature came from the ACPI thermal zone, not the EC
        public int GpuTemp = -1;
        public bool GpuShared;         // integrated GPU: GpuTemp is the CPU die temperature
        public string GpuSource = "";  // where GpuTemp came from, shown in the status column
        public int GpuUsage = -1;
        public double VramDedicatedGb = -1, VramSharedGb = -1;
        public BatteryReading Bat = new BatteryReading();
        public bool? Wifi, Bluetooth, Touchpad, Camera, Mic;   // null = not present
        public string EcError;
        public int Cpu = -1;
        public double MemUsed, MemTotal;
        public int Battery = -1;
        public bool Plugged, Charging;
        public int Brightness = -1;
        public int Volume = -1;
        public bool Muted;
        public Guid PowerMode;
        public Dictionary<AppEntry, bool> Running = new Dictionary<AppEntry, bool>();
    }

    public static class Mascot
    {
        static readonly Random rnd = new Random();

        // An urgent line, or null when nothing needs attention.
        // kind identifies the alert independently of the live number in its text.
        public static string Alert(Snapshot s, int alertTemp, out string kind)
        {
            kind = null;
            if (s.EcOk && s.Ec.FanFault) { kind = "fan"; return L.S("风扇报告了异常！建议检查一下出风口有没有堵住。", "The fan reported a fault! Check that the vents aren't blocked."); }
            if (s.Ec.CpuTemp >= alertTemp) { kind = "cpu-hot"; return L.F("好烫……CPU 已经 {0}°C 了，让电脑歇一会儿吧。", "So hot... the CPU is at {0}°C. Let the PC rest a bit.", s.Ec.CpuTemp); }
            if (!s.GpuShared && s.GpuTemp >= alertTemp) { kind = "gpu-hot"; return L.F("显卡已经 {0}°C 了，先别让它太累哦。", "The GPU is at {0}°C. Go easy on it for a while.", s.GpuTemp); }
            if (s.Battery >= 0 && s.Battery <= 10 && !s.Plugged) { kind = "battery-10"; return L.F("电量只剩 {0}% 了，再不插电我就要睡着了……", "Only {0}% left... plug in or I'm falling asleep...", s.Battery); }
            if (s.Battery >= 0 && s.Battery <= 20 && !s.Plugged) { kind = "battery-20"; return L.F("电量只剩 {0}%，快去插电吧。", "Battery's down to {0}%. Time to plug in.", s.Battery); }
            return null;
        }

        // Jokes for any machine (hardware-specific ones are in Jokes.cs).
        static string[] GeneralJokes()
        {
            if (L.En) return new[]
            {
                "Have you tried turning it off and on again?",
                "It's not a bug, it's a feature.",
                "Ctrl+Z can't undo real life. Sadly.",
                "It works on my machine. ¯\\_(ツ)_/¯",
                "Staring at a progress bar makes it slower. That's science.",
                "Have you tried downloading more RAM?",
                "Me: one more video. Also me, 3 hours later: one more video.",
                "POV: you opened Task Manager just to watch the graphs.",
                "Sigma grindset: never closing a single browser tab.",
                "Everything is fine. The room is not on fire. This is fine.",
                "Skill issue? No, CPU issue. Okay maybe skill issue.",
                "Touch grass? Never heard of her.",
            };
            if (L.Tw) return new[]
            {
                "重開機能解決 90% 的問題，剩下 10% 再重開一次。",
                "這不是 bug，是隱藏功能。",
                "Ctrl+Z 救不了人生，但救得了剛剛那一步。",
                "工程師三大謊言之一：這個改很小，不會壞。",
                "盯著進度條看，它會跑得更慢，科學證實（並沒有）。",
                "傻眼貓咪，你又開了 30 個分頁？",
                "我就爛.jpg —— 某台電腦的心聲。",
                "母湯喔，筆電不要放在棉被上。",
                "這系統穩到我都想頒獎給它了，笑死。",
                "工作做不完？沒關係，明天的你會處理（並不會）。",
            };
            return new[]
            {
                "重启能解决 90% 的问题，剩下 10% 需要再重启一次。",
                "我不是 bug，我是隐藏功能。",
                "Ctrl+Z 撤销不了现实，但能撤销刚才那一步。",
                "程序员的三大谎言之一：这个改动很小，不会出问题。",
                "据说盯着进度条看，它会走得更慢。",
            };
        }

        // Everyday care lines.
        static string[] Care()
        {
            if (L.En) return new[]
            {
                "Get up and stretch for a sec. Grab some water too.",
                "You've been staring at the screen a while. Look at something far away.",
                "Sit up straight! Your neck will thank you.",
                "Don't worry, I'm keeping an eye on the temps.",
                "Need to write something? WordPad is one click away.",
                "Saved your work? Ctrl+S is a good habit.",
                "Want to open something? Check Quick Launch on the left.",
                "Tired? Close your eyes for a minute. I've got this.",
            };
            return new[]
            {
                L.S("记得起来活动一下，喝口水。", null),
                L.S("看屏幕久了，眺望一下远处吧。", null),
                L.S("坐姿要端正哦，别把脖子累坏了。", "坐姿要端正喔，別把脖子累壞了。", null),
                L.S("我会一直帮你盯着温度。", null),
                L.S("需要写点什么吗？写字板随时可以打开。", "需要寫點什麼嗎？WordPad 隨時可以開。", null),
                L.S("保存了吗？Ctrl+S 是好习惯。", "存檔了嗎？Ctrl+S 是好習慣。", null),
                L.S("想开什么程序？左边「快捷启动」里都有。", "想開什麼程式？左邊「快速啟動」裡都有。", null),
                L.S("累了就闭眼休息一分钟，我帮你看着。", null),
            };
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        // Picks an everyday line. Candidates are keyed by template, so "CPU 52°C" and "CPU 53°C" count as the
        // same line; keys in `recent` (the last dozen shown) are skipped, so clicking doesn't cycle the same few.
        public static string Idle(Snapshot s, string biosVersion, IList<string> hardwareJokes, bool highPower, bool office, WeatherInfo weather, Queue<string> recent)
        {
            var pool = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (key, text) => pool.Add(new KeyValuePair<string, string>(key, text));
            var now = DateTime.Now;
            int h = now.Hour;

            // English: 67 is a meme. When the battery shows 67%, she can't resist (it comes back every dozen lines or so).
            if (L.En && s.Battery == 67 && !recent.Contains("67"))
                return Pick(new KeyValuePair<string, string>("67", new[] {
                    "Battery at 67%... SIX SEVEN 🤷 676767",
                    "67%?? six... SEVEN 🤷🤷 (sorry, I had to)",
                    "Battery: 67. Me: 6-7! 6-7! 676767676767",
                }[rnd.Next(3)]), recent, 24);

            // Time of day
            if (h >= 5 && h < 9) { add("t1", L.S("早上好，今天也一起加油。", "早安，今天也一起加油。", "Good morning! Let's crush it today.")); add("t2", L.S("早餐吃了吗？空着肚子可不行。", "Had breakfast? Don't run on an empty stomach.")); }
            else if (h >= 9 && h < 11) { add("t3", L.S("上午效率最高，想做的事趁现在。", "Mornings are peak focus. Do the hard stuff now.")); add("t4", L.S("上午好，今天有什么计划？", "早安，今天有什麼計畫？", "Morning! Got any plans today?")); }
            else if (h >= 11 && h < 13) { add("t5", L.S("中午了，记得好好吃饭。", "It's lunchtime. Go eat something real.")); add("t6", L.S("午休一下吧，我帮你看着电脑。", "Take a lunch break. I'll watch the PC.")); }
            else if (h >= 13 && h < 18) { add("t7", L.S("下午好，累了就伸个懒腰。", "午安，累了就伸個懶腰。", "Good afternoon. Stretch if you're tired.")); add("t8", L.S("下午茶时间到了吗？", "Is it snack o'clock yet?")); }
            else if (h >= 18 && h < 23) { add("t9", L.S("晚上好，今天辛苦了。", "晚安～今天辛苦了。", "Good evening. You did great today.")); add("t10", L.S("晚上就别太拼了，早点放松。", "Evenings are for chilling. Don't overdo it.")); }
            else { add("t11", L.S("夜深了，早点休息哦。", "夜深了，早點休息喔。", "It's late. Go to bed soon, okay?")); add("t12", L.S("这么晚还不睡？我都要困了……", "Still up? Even I'm getting sleepy...")); }
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) add("w1", L.S("周末也在用电脑呀，记得出去走走。", "On the PC on a weekend? Go touch some grass too."));
            else if (now.DayOfWeek == DayOfWeek.Friday) add("w2", L.S("星期五了，再坚持一下就是周末！", "It's Friday! The weekend is so close!"));
            else if (now.DayOfWeek == DayOfWeek.Monday) add("w3", L.S("星期一，元气满满地开始吧。", "Monday again... we ball."));

            // Temperatures and fan
            int t = s.Ec.CpuTemp;
            if (t >= 0)
            {
                if (L.En && t == 67) add("cpu-t", "CPU at 67°C... six seven 🤷");
                else if (L.En && t == 69) add("cpu-t", "CPU at 69°C. Nice.");
                else add("cpu-t", t < 50 ? L.F("CPU 才 {0}°C，冰冰凉。", "CPU is only {0}°C. Ice cold.", t)
                                 : t < 70 ? L.F("CPU {0}°C，状态不错。", "CPU at {0}°C. Looking good.", t)
                                 : L.F("CPU {0}°C，稍微有点热。", "CPU at {0}°C. Getting a bit warm.", t));
            }
            if (s.EcOk)
            {
                if (s.Ec.FanRpm > 3500) add("fan", L.F("风扇转得有点快，{0} 转，电脑在努力散热。", "Fan's going fast, {0} RPM. Working hard to cool down.", s.Ec.FanRpm));
                else if (s.Ec.FanRpm > 0) add("fan", L.F("风扇正以 {0} 转安静地工作。", "The fan is humming quietly at {0} RPM.", s.Ec.FanRpm));
                else if (s.Ec.FanRpm == 0) add("fan", L.S("风扇在休息，温度低的时候它会停下来。", "The fan is napping. It stops when things are cool."));
            }
            else add("fan", L.S("风扇数据暂时还读不到。", "Can't read the fan right now."));
            if (s.GpuTemp > 0)
            {
                if (s.GpuShared) add("gpu-t", L.F("显卡和 CPU 在同一块芯片上，现在 {0}°C。", "The GPU lives on the same chip as the CPU. {0}°C right now.", s.GpuTemp));
                else add("gpu-t", s.GpuTemp < 70 ? L.F("GPU {0}°C，显卡很冷静。", "GPU at {0}°C. Chill as ever.", s.GpuTemp) : L.F("GPU {0}°C，显卡在努力干活呢。", "GPU at {0}°C. It's grinding.", s.GpuTemp));
            }

            // Load
            if (s.Cpu >= 80) add("cpu-u", L.F("CPU 占用 {0}%，在忙大事吗？", "CPU at {0}%. Doing something big?", s.Cpu));
            else if (s.Cpu >= 0 && s.Cpu < 10) add("cpu-u", L.F("CPU 占用才 {0}%，电脑在悄悄摸鱼。", "CPU at {0}%. Your PC is lowkey slacking off.", s.Cpu));
            if (s.GpuUsage >= 70) add("gpu-u", L.F("显卡正在全力工作，占用 {0}%。", "The GPU is going all out: {0}% busy.", s.GpuUsage));
            if (s.MemTotal > 0)
                add("mem", s.MemUsed / s.MemTotal > 0.85
                    ? L.F("内存用了 {0} GB，快满了，关掉些不用的程序吧。", "{0} GB of RAM used, almost full. Close some stuff... or download more RAM.", s.MemUsed.ToString("F1"))
                    : L.F("内存用了 {0} GB，一切从容。", "{0} GB of RAM used. All chill.", s.MemUsed.ToString("F1")));

            // Battery
            var b = s.Bat;
            if (s.Battery >= 0)
            {
                if (s.Charging) add("bat", L.F("正在充电，现在 {0}%。", "Charging, now at {0}%.", s.Battery) + (b.ToFullMin > 0 ? L.F("大约 {0}充满。", " About {0} to full.", L.Duration(b.ToFullMin)) : ""));
                else if (s.Plugged && s.Battery >= 95) add("bat", L.S("电池已经充满啦。", "Battery's full!"));
                else if (!s.Plugged) add("bat", L.F("正在用电池，还剩 {0}%。", "On battery, {0}% left.", s.Battery) + (b.RemainingMin > 0 ? L.F("大概还能用 {0}。", " About {0} to go.", L.Duration(b.RemainingMin)) : ""));
                if (L.En && s.Battery == 69) add("bat69", "Battery at 69%. Nice.");
            }
            if (b.HealthPercent > 0)
                add("bat-h", b.HealthPercent >= 90 ? L.F("电池健康度 {0}%，保养得不错。", "Battery health {0}%. Well kept!", Math.Round(b.HealthPercent))
                                                    : L.F("电池健康度 {0}%，有点老化了，别长期满电插着。", "Battery health {0}%. It's aging; don't keep it plugged in at 100% all the time.", Math.Round(b.HealthPercent)));
            if (b.CycleCount > 0) add("bat-c", L.F("电池已经完整充放电 {0} 次了。", "The battery has been through {0} full cycles.", b.CycleCount));
            if (b.RateMW < -1000) add("bat-w", L.F("现在耗电大约 {0} W。", "Drawing about {0} W right now.", (-b.RateMW / 1000.0).ToString("F1")));

            // Gaming laptops on battery: 雌小鬼-style teasing (杂鱼～), always about battery and performance.
            if (highPower && s.Battery >= 0 && !s.Plugged)
            {
                add("bat-tease", L.F3("宝宝拔电了？这配置撑不了多久，快去找插座～现在还有 {0}%。", "寶寶拔電了？這配置撐不了多久喔，快去找插座～還剩 {0}%。",
                    "Unplugged, baby? This build won't last. Run to an outlet~ {0}% left.", s.Battery));
                if (s.Battery < 50) add("bat-tease2", L.F3("才用了一会儿就只剩 {0}% 了？宝宝这就不行了，杂鱼～", "才用一下就剩 {0}%？寶寶這樣就不行了喔，雜魚～♡",
                    "Only {0}% already? Is baby tired? Zako~ ♡", s.Battery));
                else add("bat-tease2", L.F3("宝宝坚持住～电量还有 {0}%，再撑一下下～", "寶寶撐住～還有 {0}%，再一下下就好～",
                    "Hang in there, baby~ {0}% left, just a little longer~", s.Battery));
                if (b.RemainingMin > 0) add("bat-tease3", L.F3("宝宝还能撑 {0}？已经很棒了哦～", "寶寶還能撐 {0}？已經很棒了喔～",
                    "Baby can last {0} more? That's actually so good~ ♡", L.Duration(b.RemainingMin)));
            }
            if (highPower && s.Ec.CpuTemp >= 80)
                add("hot-tease", L.F3("宝宝都 {0}°C 了还不休息，好努力哦～", "寶寶都 {0}°C 了還不休息，好努力喔～", "{0}°C and still going? Baby's trying so hard~", s.Ec.CpuTemp));

            // Office laptops: the opposite tease — praise for the long battery life (it's always the battery).
            if (office && s.Battery >= 0)
            {
                add("office1", L.S("宝宝好大啊！续航好久，杂鱼好喜欢！（当然我说的是电池）", "寶寶好大喔！續航超久，雜魚好喜歡！（我說的當然是電池）",
                    "Baby's so big! Lasts forever and zako loves it! (The battery. Obviously.)"));
                add("office2", L.S("办公本宝宝，一整天不插电都没问题，好持久～（说的是续航啦）", "文書機寶寶一整天不插電都沒問題，好持久～（說的是續航啦）",
                    "Office laptop baby goes all day unplugged. Such stamina~ (battery life!)"));
                add("office3", L.S("宝宝不插电也能坚持这么久，好厉害！（电池，是电池！）", "寶寶不插電也能撐這麼久，好厲害！（電池，是電池！）",
                    "Lasting this long without a charger? Impressive! (Battery. It's the battery.)"));
                add("office4", L.S("游戏本宝宝 30 分钟就不行了，你可以坚持一整天哦～（续航）", "電競筆電寶寶 30 分鐘就不行了，你可以撐一整天喔～（續航）",
                    "Gaming laptops tap out after 30 minutes. You go all day~ (battery life)"));
                add("office5", L.S("轻轻薄薄的，带出门刚刚好～（说的是笔记本）", "輕輕薄薄的，帶出門剛剛好～（說的是筆電）",
                    "Thin, light, perfect to take out~ (the laptop!)"));
                if (b.DesignMWh >= 50000)
                    add("office-cap", L.F3("宝宝的电池有 {0} Wh，好大啊！（真的是电池）", "寶寶的電池有 {0} Wh，好大喔！（真的是電池）",
                        "A {0} Wh battery? So big! (Really, the battery.)", (b.DesignMWh / 1000.0).ToString("F0")));
                else if (b.DesignMWh > 0)
                    add("office-cap", L.F3("电池 {0} Wh，宝宝小小的，也很能撑呢～（电池！）", "電池 {0} Wh，寶寶小小的，也很能撐呢～（電池！）",
                        "{0} Wh. Small, but baby really holds on~ (the battery!)", (b.DesignMWh / 1000.0).ToString("F0")));
                if (!s.Plugged && b.RemainingMin >= 180)
                    add("office-long", L.F3("宝宝还能撑 {0}，好持久～杂鱼好喜欢！（当然说的是电池）", "寶寶還能撐 {0}，好持久～雜魚好喜歡！（當然說的是電池）",
                        "{0} left and still going strong~ zako loves it! (the battery, obviously)", L.Duration(b.RemainingMin)));
                if (s.Charging)
                    add("office-chg", L.S("插上电就乖乖充电，宝宝好听话～", "插上電就乖乖充電，寶寶好聽話～", "Plugged in and charging like a good baby~"));
            }

            // How long Windows has been running
            int upMin = (int)(GetTickCount64() / 60000);
            if (upMin >= 24 * 60) add("up", L.F3("电脑已经连续开机 {0} 小时了，偶尔重启一下会更清爽。", "電腦已經連續開機 {0} 小時了，偶爾重新開機會更清爽。", "Up for {0} hours straight. A restart now and then keeps things fresh.", upMin / 60));
            else if (upMin >= 30) add("up", L.F("这次开机已经 {0}了。", "This session has been up for {0}.", L.Duration(upMin)));

            // Weather
            if (weather != null && weather.Error == null && weather.Code >= 0)
            {
                string where = weather.Place ?? L.S("外面", "your area");
                if (weather.Rain) add("wx", L.F("{0}今天有{1}，出门记得带伞。", "{1} in {0} today. Bring an umbrella!", where, weather.Sky));
                else if (weather.Snow) add("wx", L.F("{0}今天下雪，路上小心。", "Snow in {0} today. Careful out there.", where));
                else if (weather.Clear) add("wx", L.F("{0}今天是晴天，适合出去走走。", "Clear skies in {0} today. Perfect for a walk.", where));
                else add("wx", L.F("{0}今天{1}，{2} 到 {3}°C。", "{1} in {0} today, {2} to {3}°C.", where, weather.Sky, weather.Min, weather.Max));
                if (weather.Max >= 30) add("wx-hot", L.F("今天最高 {0}°C，注意防暑，多喝水。", "High of {0}°C today. Stay hydrated!", weather.Max));
                else if (weather.Min <= 5) add("wx-cold", L.F("今天最低只有 {0}°C，多穿点。", "Low of just {0}°C today. Bundle up.", weather.Min));
            }

            if (!string.IsNullOrEmpty(biosVersion)) add("bios", L.F3("BIOS 是 {0}，建议看看是不是最新哦。", "BIOS 是 {0}，建議看看是不是最新版喔。", "BIOS is {0}. Maybe check if it's the latest?", biosVersion));
            var care = Care();
            for (int i = 0; i < care.Length; i++) add("care" + i, care[i]);
            var jokes = GeneralJokes();
            for (int i = 0; i < jokes.Length; i++) add("joke" + i, jokes[i]);
            // GPU / CPU jokes for this machine's actual hardware (Jokes.cs)
            for (int i = 0; i < hardwareJokes.Count; i++) add("hw" + i, hardwareJokes[i]);

            var fresh = pool.Where(p => !recent.Contains(p.Key)).ToList();
            return Pick((fresh.Count > 0 ? fresh : pool)[rnd.Next(fresh.Count > 0 ? fresh.Count : pool.Count)], recent, Math.Min(12, pool.Count / 2));
        }

        static string Pick(KeyValuePair<string, string> line, Queue<string> recent, int keep)
        {
            recent.Enqueue(line.Key);
            while (recent.Count > Math.Max(1, keep)) recent.Dequeue();
            return line.Value;
        }
    }
}
