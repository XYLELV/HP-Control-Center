// Loads MainWindow.xaml (embedded) and wires every control to the hardware/launcher/settings code.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace HPControlPanel
{
    public class MainWindow
    {
        public readonly Window W;
        readonly Settings settings = Settings.Load();
        readonly Machine machine = Machine.Detect();
        EcProfile profile;
        bool gpuIntegrated;
        double vramTotalGb;
        readonly Ec ec = new Ec();
        bool ecOk;
        Timer pollTimer;
        int polling;
        int pollCount;
        volatile bool forceSlowPoll = true;   // first poll reads everything
        Snapshot last = new Snapshot();
        readonly List<KeyValuePair<DateTime, int>> history = new List<KeyValuePair<DateTime, int>>();
        readonly List<KeyValuePair<DateTime, int>> gpuHistory = new List<KeyValuePair<DateTime, int>>();   // only for GPUs with their own sensor
        List<AppEntry> apps;
        string biosVersion { get { return machine.Bios; } }
        bool suppress;                   // true while code (not the user) moves a control
        DateTime userTouchedBrightness, userTouchedVolume;
        readonly DispatcherTimer brightnessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int pendingBrightness = -1;
        string currentLine = "", currentAlert, currentAlertKind;
        DateTime lineChanged = DateTime.MinValue;
        const double LineSeconds = 5;     // HP 娘 switches to a new everyday line this often

        // Hardware table cells
        TextBlock hwCpuTempV, hwCpuTempS, hwGpuTempV, hwGpuTempS, thGpu, hwCpuV, hwCpuS, hwGpuV, hwGpuS, hwMemV, hwMemS, hwVramV, hwVramS, hwBoardV, hwBoardS, hwFanV, hwFanS;
        TextBlock thCpu, thFan, thPassive, thCritical, thBoard, thGpuNote;
        TextBlock batHealthV, batHealthS, batCapV, batCapS, batCycleV, batCycleS, batNowV, batNowS, batPowerV, batPowerS, batTimeV, batTimeS;

        T Get<T>(string name) where T : class { return (T)W.FindName(name); }

        public MainWindow()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                W = (Window)XamlReader.Load(s);
            W.FontFamily = new FontFamily(L.FontFamily);
            L.Localize(W);

            W.Icon = LoadResourceImage("app.ico");
            Get<Image>("SideLogo").Source = LoadResourceImage("app-icon.png");
            profile = EcProfile.For(machine);
            gpuIntegrated = Gpu.AllIntegrated(machine.Gpu);
            Gpu.InitCounters();
            vramTotalGb = Gpu.DedicatedTotalGb();
            FitToScreen();
            W.Topmost = settings.TopMost;
            ApplyTheme(settings.Theme);
            ReadMachineInfo();

            BuildHardwareRows();
            BuildThermalRows();
            WireNavigation();
            WireControls();
            WirePowerPlans();
            WireSystemActions();
            WireSettingsPage();
            WireLanguage();
            LoadApps();
            Devices.Detect();
            BuildToggles();
            BuildBatteryRows();
            LoadMascotImage();
            StartAnimations();
            StartWeather();
            ecOk = ec.Open(profile, Path.Combine(Settings.Dir, "LpcACPIEC.bin"));   // before LoadDetails, which reads it
            LoadDetails();
            WireUpdates();

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (o, e) => UpdateClock();
            clock.Start();
            UpdateClock();

            Get<Canvas>("ChartCanvas").SizeChanged += (o, e) => DrawChart();
            W.Closed += (o, e) => { if (pollTimer != null) pollTimer.Dispose(); ec.Dispose(); };
            RestartPolling();
        }

        // Layout check: render the window to a PNG after the first readings arrive, then exit.
        public void SnapshotAndExit(string path, string nav)
        {
            // Same size as a maximized window, but placed off-screen and never activated.
            var wa = SystemParameters.WorkArea;
            W.WindowState = WindowState.Normal;
            W.WindowStartupLocation = WindowStartupLocation.Manual;
            W.Width = wa.Width; W.Height = wa.Height;
            W.Left = -wa.Width - 2000; W.Top = 0;
            W.ShowActivated = false; W.ShowInTaskbar = false;
            if (!string.IsNullOrEmpty(nav)) Get<RadioButton>(nav).IsChecked = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (o, e) =>
            {
                t.Stop();
                var root = (FrameworkElement)W.Content;
                var dpi = VisualTreeHelper.GetDpi(root);
                var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(W.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bmp.Render(bg);
                bmp.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                // Encoded in memory, then written through SafeFile like every other write of this elevated app.
                var png = new MemoryStream();
                enc.Save(png);
                if (!SafeFile.WriteAllBytes(path, png.ToArray()) || !SafeFile.WriteAllText(path + ".txt", "client " + root.ActualWidth + "x" + root.ActualHeight + " dpi " + dpi.PixelsPerInchX))
                    App.Log(new IOException("snapshot refused (folder or file is a link): " + path));
                Application.Current.Shutdown();
            };
            t.Start();
        }

        // Opens a page by its nav button name (after a restart the app comes back on the page it left).
        public void ShowPage(string nav)
        {
            var rb = nav != null && nav.StartsWith("Nav") ? W.FindName(nav) as RadioButton : null;
            if (rb != null) rb.IsChecked = true;
        }

        // ================= Startup helpers =================

        // Design size of the layout; on smaller windows the whole UI is scaled down instead of scrolling.
        const double DesignW = 1180, DesignH = 640;

        void FitToScreen()
        {
            var wa = SystemParameters.WorkArea;
            if (wa.Width < 1400 || wa.Height < 820) W.WindowState = WindowState.Maximized;
            else { W.Width = Math.Min(1400, wa.Width - 40); W.Height = Math.Min(860, wa.Height - 40); }

            var host = Get<Grid>("Host");
            var root = Get<Grid>("Root");
            host.SizeChanged += (o, e) =>
            {
                double w = host.ActualWidth, h = host.ActualHeight;
                if (w < 1 || h < 1) return;
                double s = Math.Min(1, Math.Min(w / DesignW, h / DesignH));
                root.LayoutTransform = s < 1 ? new ScaleTransform(s, s) : Transform.Identity;
                root.Width = w / s;
                root.Height = h / s;
            };
        }

        void ReadMachineInfo()
        {
            var m = machine;
            var parts = new List<string> { m.ShortModel };
            if (m.Cpu.Length > 0) parts.Add(m.Cpu);
            if (m.RamGb > 0) parts.Add(m.RamGb + " GB");
            if (m.Gpu.Length > 0) parts.Add(L.S("显卡：", "顯示卡：", "GPU: ") + m.Gpu);
            Get<TextBlock>("ModelText").Text = string.Join(" · ", parts);
            Get<TextBlock>("SideModelText").Text = m.ShortModel;
            string pc = m.Product.StartsWith(m.Maker, StringComparison.OrdinalIgnoreCase) ? m.Product : (m.Maker + " " + m.Product).Trim();
            Get<TextBlock>("AboutText").Text =
                L.F("HP 控制台 {0} · 作者 {1}（个人自制，非 HP 官方软件）", "HP Control Center {0} · by {1} (personal project, not official HP software)", Updater.Version, Updater.Author) + "\n" +
                pc + L.S(" · 主板 ", " · Board ") + m.Board + " · BIOS " + m.Bios + "\n" +
                (profile != null
                    ? L.F("风扇与温度：已适配（{0}），通过 PawnIO 只读读取 EC，不会写入任何硬件设置。", "Fan & temperatures: supported ({0}), read-only from the EC via PawnIO. Nothing is ever written to the hardware.", profile.Name)
                    : L.S("风扇与温度：此机型尚未适配风扇读取，CPU 温度来自 Windows 的 ACPI 温度区。", "Fan & temperatures: fan reading isn't supported on this model yet; CPU temperature comes from the Windows ACPI thermal zone."));
            Get<TextBlock>("ThermalNote").Text = profile != null
                ? L.S("以下数据通过 PawnIO 从主板嵌入式控制器（EC）只读读取，本程序不会写入任何风扇设置。", "Read-only from the embedded controller (EC) via PawnIO. This app never writes any fan setting.")
                : L.F("这台电脑的主板（{0}）还没有适配风扇读取，只能显示 Windows 提供的 ACPI 温度。", "Fan reading isn't supported on this board ({0}) yet; only the Windows ACPI temperature is shown.", m.Board);
        }

        static BitmapSource LoadResourceImage(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                if (name.EndsWith(".ico")) return BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var b = new BitmapImage();
                b.BeginInit(); b.StreamSource = s; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                return b;
            }
        }

        // The custom picture in the user-writable data folder: read through SafeFile with a size cap, and refused if
        // it decodes to something absurdly large (a tiny file can claim a huge image and exhaust memory).
        static BitmapSource LoadFileImage(string path)
        {
            try
            {
                byte[] data = SafeFile.ReadAllBytes(path, Settings.MaxImageBytes);
                if (data == null) return null;
                var frame = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 40000000L) return null;
                var b = new BitmapImage();
                b.BeginInit(); b.StreamSource = new MemoryStream(data); b.CacheOption = BitmapCacheOption.OnLoad; b.CreateOptions = BitmapCreateOptions.IgnoreImageCache; b.EndInit(); b.Freeze();
                return b;
            }
            catch { return null; }
        }

        void ApplyTheme(string theme)
        {
            Color accent, soft;
            switch (theme)
            {
                case "pink": accent = Color.FromRgb(0xD9, 0x4F, 0x8C); soft = Color.FromRgb(0xFB, 0xEA, 0xF2); break;
                case "purple": accent = Color.FromRgb(0x7A, 0x5A, 0xD6); soft = Color.FromRgb(0xF0, 0xEC, 0xFB); break;
                default: theme = "blue"; accent = Color.FromRgb(0x0F, 0x6C, 0xBD); soft = Color.FromRgb(0xEA, 0xF2, 0xFB); break;
            }
            W.Resources["AccentBrush"] = new SolidColorBrush(accent);
            W.Resources["AccentSoftBrush"] = new SolidColorBrush(soft);
            settings.Theme = theme;
            if (history.Count > 0) DrawChart();
        }

        // ================= Navigation =================

        void WireNavigation()
        {
            var pages = new[] {
                new { Nav = "NavHome", Page = "PageHome", Title = L.T("系统概览") },
                new { Nav = "NavDetails", Page = "PageDetails", Title = L.T("详细配置") },
                new { Nav = "NavDisplay", Page = "PageDisplay", Title = L.T("屏幕与声音") },
                new { Nav = "NavPower", Page = "PagePower", Title = L.T("电源与电池") },
                new { Nav = "NavApps", Page = "PageApps", Title = L.T("快捷启动") },
                new { Nav = "NavSettings", Page = "PageSettings", Title = L.T("设置") },
            };
            foreach (var p in pages)
            {
                var page = p;
                Get<RadioButton>(page.Nav).Checked += (o, e) =>
                {
                    foreach (var q in pages) Get<UIElement>(q.Page).Visibility = q.Page == page.Page ? Visibility.Visible : Visibility.Collapsed;
                    Get<TextBlock>("PageTitleText").Text = page.Title;
                };
            }
            Get<Button>("RefreshBtn").Click += (o, e) => ThreadPool.QueueUserWorkItem(_ => Poll());
        }

        // ================= Hardware table =================

        void AddRow(Panel host, string label, out TextBlock value, out TextBlock status, bool last = false, double padding = 0)
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label });
            value = new TextBlock { Text = "—", HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(value, 1); g.Children.Add(value);
            status = new TextBlock { Text = "", FontSize = 13, Foreground = (Brush)W.Resources["SubBrush"], HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(status, 2); g.Children.Add(status);
            host.Children.Add(new Border { Child = g, Padding = new Thickness(0, padding, 0, padding), BorderBrush = (Brush)W.Resources["RowLineBrush"], BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) });
        }

        void BuildHardwareRows()
        {
            var host = Get<Panel>("HwRows");
            AddRow(host, L.S("CPU 温度", "CPU temp"), out hwCpuTempV, out hwCpuTempS);
            AddRow(host, L.S("GPU 温度", "GPU temp"), out hwGpuTempV, out hwGpuTempS);
            AddRow(host, L.S("CPU 占用", "CPU 使用率", "CPU usage"), out hwCpuV, out hwCpuS);
            AddRow(host, L.S("GPU 占用", "GPU 使用率", "GPU usage"), out hwGpuV, out hwGpuS);
            AddRow(host, L.S("内存使用", "Memory"), out hwMemV, out hwMemS);
            AddRow(host, L.S("显存占用", "顯示記憶體", "VRAM"), out hwVramV, out hwVramS);
            AddRow(host, L.S("主板温度", "Board temp"), out hwBoardV, out hwBoardS);
            AddRow(host, L.S("风扇", "Fan"), out hwFanV, out hwFanS, true);
        }

        static string NotSupported { get { return L.S("此机型未适配", "此機型未支援", "Not supported on this model"); } }

        void BuildThermalRows()
        {
            var host = Get<Panel>("ThermalRows");
            var p = profile;
            Func<byte, string, string> reg = (addr, name) => p != null ? name + " · 0x" + addr.ToString("X2") : NotSupported;
            TextBlock note;
            AddRow(host, L.S("CPU 温度", "CPU temp"), out thCpu, out note, false, 6); note.Text = p != null ? reg(p.CpuTemp, "EC") : L.S("ACPI 温度区", "ACPI thermal zone");
            AddRow(host, L.S("GPU 温度", "GPU temp"), out thGpu, out note, false, 6); thGpuNote = note;
            AddRow(host, L.S("主板温度", "Board temp"), out thBoard, out note, false, 6); note.Text = p != null ? reg(p.BoardTemp, "EC") : NotSupported;
            AddRow(host, L.S("风扇转速", "Fan speed"), out thFan, out note, false, 6); note.Text = p != null ? reg(p.FanRpmLo, "EC") : NotSupported;
            AddRow(host, L.S("降频温度", "Throttle temp"), out thPassive, out note, false, 6); note.Text = L.S("超过就降频", "Slows down above this");
            AddRow(host, L.S("强制关机温度", "Shutdown temp"), out thCritical, out note, true, 6); note.Text = L.S("超过就关机保护", "Shuts off above this");
        }

        void BuildBatteryRows()
        {
            var host = Get<Panel>("BatteryRows");
            AddRow(host, L.S("健康度", "Health"), out batHealthV, out batHealthS, false, 6);
            AddRow(host, L.S("满充容量", "Full charge"), out batCapV, out batCapS, false, 6);
            AddRow(host, L.S("循环次数", "Cycles"), out batCycleV, out batCycleS, false, 6);
            AddRow(host, L.S("当前电量", "目前電量", "Remaining"), out batNowV, out batNowS, false, 6);
            AddRow(host, L.S("功率", "Power"), out batPowerV, out batPowerS, false, 6);
            AddRow(host, L.S("预计时间", "Time"), out batTimeV, out batTimeS, true, 6);
        }

        static string Wh(int mWh) { return (mWh / 1000.0).ToString("F2") + " Wh"; }

        static string About(int minutes) { return L.S("约 ", "~") + L.Duration(minutes); }

        void ApplyBattery(Snapshot s)
        {
            var b = s.Bat;
            string noDriver = L.S("电池驱动未提供", "Not reported by the driver");
            string calc = L.S("计算中", "estimating");
            batHealthV.Text = b.HealthPercent > 0 ? Math.Round(b.HealthPercent) + "%" : "—";
            batHealthS.Text = b.HealthPercent > 0 ? L.S("满充容量 ÷ 设计容量", "Full charge ÷ design") : noDriver;
            batCapV.Text = b.FullMWh > 0 ? Wh(b.FullMWh) : "—";
            batCapS.Text = b.DesignMWh > 0 ? L.S("设计 ", "Design ") + Wh(b.DesignMWh) : "";
            batCycleV.Text = b.CycleCount > 0 ? b.CycleCount + L.S(" 次", "") : "—";
            batCycleS.Text = b.CycleCount > 0 ? L.S("完整充放电的次数", "Full charge cycles") : noDriver;
            batNowV.Text = b.NowMWh >= 0 ? Wh(b.NowMWh) : "—";
            batNowS.Text = b.VoltageMV > 0 ? L.S("电压 ", "Voltage ") + (b.VoltageMV / 1000.0).ToString("F2") + " V" : "";
            if (b.RateMW != 0) { batPowerV.Text = (Math.Abs(b.RateMW) / 1000.0).ToString("F1") + " W"; batPowerS.Text = (b.RateMW > 0 ? L.S("充电", "Charging") : L.S("耗电", "Draining")) + " · " + b.RateSource; }
            else { batPowerV.Text = "—"; batPowerS.Text = b.Charging || b.Discharging ? L.S("计算中（约 20 秒）", "Estimating (about 20 s)") : L.S("未在充放电", "Not charging or draining"); }
            if (b.Discharging) { batTimeV.Text = b.RemainingMin > 0 ? About(b.RemainingMin) : "—"; batTimeS.Text = b.RemainingMin > 0 ? L.S("按当前耗电还能用", "Left at the current draw") : L.S("还能用多久：", "Time left: ") + calc; }
            else if (b.Charging) { batTimeV.Text = b.ToFullMin > 0 ? About(b.ToFullMin) : "—"; batTimeS.Text = b.ToFullMin > 0 ? L.S("预计多久充满", "Until full") : L.S("多久充满：", "Until full: ") + calc; }
            else { batTimeV.Text = b.Online && s.Battery >= 95 ? L.S("已充满", "Full") : "—"; batTimeS.Text = b.Online ? L.S("已接通电源", "Plugged in") : ""; }
        }

        // ================= Quick switches =================

        class Tile
        {
            public ToggleButton Btn; public TextBlock State;
            public Func<Snapshot, bool?> Get; public Func<bool, bool> Set; public string ConfirmOff;
            public bool Busy, Done;
        }
        readonly List<Tile> tiles = new List<Tile>();

        void BuildToggles()
        {
            AddTile("WLAN", s => s.Wifi, on => Radios.Set(Radios.WiFi, on), null);
            AddTile(L.S("蓝牙", "Bluetooth"), s => s.Bluetooth, on => Radios.Set(Radios.Bluetooth, on), null);
            AddTile(L.S("触控板", "Touchpad"), s => s.Touchpad, on => Devices.SetEnabled(Devices.TouchpadId, on),
                L.S("关闭后触控板会停止工作。之后要用外接鼠标，或用键盘（Tab 选中这个开关，按空格）重新打开。\n\n确定关闭触控板吗？",
                    "關閉後觸控板會停止運作。之後要用外接滑鼠，或用鍵盤（按 Tab 選到這個開關，再按空白鍵）重新開啟。\n\n確定要關閉觸控板嗎？",
                    "The touchpad stops working once it's off. To turn it back on, use an external mouse, or the keyboard (Tab to this switch, then Space).\n\nTurn off the touchpad?"));
            AddTile(L.S("摄像头", "Camera"), s => s.Camera, on => Devices.SetEnabled(Devices.CameraId, on), null);
            AddTile(L.S("麦克风", "Mic"), s => s.Mic, on => { SystemInfo.SetMic(on); return true; }, null);
            UpdateTiles(last);
        }

        void AddTile(string name, Func<Snapshot, bool?> get, Func<bool, bool> set, string confirmOff)
        {
            var g = new Grid();
            g.Children.Add(new TextBlock { Text = name });
            var state = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12, Foreground = (Brush)W.Resources["SubBrush"] };
            g.Children.Add(state);
            var t = new Tile { Btn = new ToggleButton { Content = g, Style = (Style)W.Resources["Tile"] }, State = state, Get = get, Set = set, ConfirmOff = confirmOff };
            t.Btn.Click += (o, e) =>
            {
                bool? cur = t.Get(last);
                if (cur == null || t.Busy) { UpdateTiles(last); return; }
                bool want = !cur.Value;
                if (!want && t.ConfirmOff != null && MessageBox.Show(W, t.ConfirmOff, name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                { UpdateTiles(last); return; }
                t.Busy = true;
                UpdateTiles(last);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    bool ok = false;
                    try { ok = t.Set(want); }
                    catch (Exception ex) { App.Log(ex); }   // an exception here would otherwise end the process
                    t.Done = true;   // cleared on the next reading, so the tile shows the real new state
                    forceSlowPoll = true;
                    if (!ok) W.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(W, L.F("{0}切换失败。", "Couldn't switch {0}.", name), name)));
                    else if (Devices.TakeRestartNeeded())
                        W.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(W, L.F("{0}已切换，重启电脑后生效。", "{0} was switched. It takes effect after a restart.", name), name)));
                    Poll();
                });
            };
            tiles.Add(t);
            Get<Panel>("ToggleTiles").Children.Add(t.Btn);
        }

        void UpdateTiles(Snapshot s)
        {
            foreach (var t in tiles)
            {
                if (t.Busy && t.Done) { t.Busy = false; t.Done = false; }
                bool? on = t.Get(s);
                t.Btn.IsChecked = on == true;
                t.Btn.IsEnabled = on != null && !t.Busy;
                t.State.Text = t.Busy ? "…" : on == null ? L.S("无", "—") : on.Value ? L.S("开", "On") : L.S("关", "Off");
                t.State.Foreground = on == true ? (Brush)W.Resources["AccentBrush"] : (Brush)W.Resources["SubBrush"];
            }
        }

        // ================= Updates =================

        // Quiet: first look a minute after start, then hourly ticks that only actually query GitHub when the
        // last check is over a day old. A failed automatic check says nothing; only the manual button reports errors.
        void WireUpdates()
        {
            var status = Get<TextBlock>("UpdateStatus");
            status.Text = L.T("当前版本") + " " + Updater.Version;
            var sw = Get<CheckBox>("AutoUpdateSwitch");
            sw.IsChecked = settings.AutoUpdate;
            sw.Click += (o, e) => { settings.AutoUpdate = sw.IsChecked == true; settings.Save(); };
            Get<Button>("CheckUpdateBtn").Click += (o, e) => CheckForUpdate(true);
            var gh = Get<Button>("GitHubBtn");
            if (Updater.RepoUrl.Length == 0) gh.Visibility = Visibility.Collapsed;
            else
            {
                gh.ToolTip = Updater.RepoUrl;
                gh.Click += (o, e) => OpenUrl(Updater.RepoUrl);
            }
            if (App.SnapshotMode) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += (o, e) =>
            {
                timer.Interval = TimeSpan.FromHours(1);
                if (settings.AutoUpdate && (DateTime.UtcNow - new DateTime(settings.LastUpdateCheck, DateTimeKind.Utc)).TotalHours >= 24)
                    CheckForUpdate(false);
            };
            timer.Start();
        }

        // Opens un-elevated in the default browser. Only https links to github.com (the repository and its releases;
        // the release URL comes from the GitHub API), re-serialized by Uri so no quote can reach explorer's command line.
        static void OpenUrl(string url)
        {
            Uri u;
            if (Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort
                && string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase) && u.AbsoluteUri.IndexOf('"') < 0)
                Launcher.ShellOpen("\"" + u.AbsoluteUri + "\"");
        }

        void CheckForUpdate(bool manual)
        {
            var status = Get<TextBlock>("UpdateStatus");
            if (manual) status.Text = L.S("正在检查…", "Checking…");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = Updater.Check();
                W.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Only a check that reached GitHub counts; after a failure (e.g. no network yet at sign-in)
                    // the hourly tick tries again instead of waiting a day.
                    if (r.Error == null) settings.LastUpdateCheck = DateTime.UtcNow.Ticks;
                    if (r.Newer)
                    {
                        status.Inlines.Clear();
                        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(L.F("发现新版本 v{0}，点这里查看 ›", "Version {0} is out, click to see it ›", r.Latest)));
                        string url = r.Url;
                        link.Click += (o, e) => OpenUrl(url);
                        status.Inlines.Add(link);
                        Get<UIElement>("UpdateDot").Visibility = Visibility.Visible;
                        if (settings.AnnouncedVersion != r.Latest)   // HP 娘 mentions each new version once
                        {
                            settings.AnnouncedVersion = r.Latest;
                            ShowLine(L.F("发现新版本 v{0} 啦～在「设置 → 关于」里可以看到哦。", "New version {0} is out~ Check Settings → About!", r.Latest));
                        }
                    }
                    else if (r.Error == null) status.Text = L.F("已是最新版本（{0}）", "You're up to date ({0})", Updater.Version);
                    else if (manual) status.Text = L.T("当前版本") + " " + Updater.Version + " · " + r.Error;
                    settings.Save();
                }));
            });
        }

        // ================= 详细配置 =================

        string detailsText = "";

        // WMI hardware queries take a second or two, so they run off the UI thread once at startup.
        void LoadDetails()
        {
            Get<Button>("CopyDetailsBtn").Click += (o, e) =>
            {
                if (detailsText.Length == 0) return;
                try { Clipboard.SetText(detailsText); Get<TextBlock>("DetailsStatus").Text = L.S("已复制到剪贴板。", "Copied to the clipboard."); }
                catch { Get<TextBlock>("DetailsStatus").Text = L.S("剪贴板正被其他程序占用，请再点一次。", "The clipboard is busy in another program. Please try again."); }
            };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Same EC-corrected battery values as the power page (ec is opened before this runs).
                EcSnapshot e = null;
                if (ecOk) { var r0 = ec.Read(); if (r0.CpuTemp >= 0) e = r0; }
                var rows = SystemDetails.Collect(machine, ReadBattery(e));
                W.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var grid = Get<Grid>("DetailsGrid");
                    if (L.En) grid.ColumnDefinitions[0].Width = new GridLength(124);   // "Motherboard:" is wider than two CJK characters
                    foreach (var r in rows)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        int row = grid.RowDefinitions.Count - 1;
                        var label = new TextBlock { Text = r.Key + L.S("：", ":"), FontSize = 15, Foreground = (Brush)W.Resources["SubBrush"], Margin = new Thickness(0, 5, 0, 5) };
                        var value = new TextBlock { Text = string.Join("\n", r.Value), FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), LineHeight = 24 };
                        Grid.SetRow(label, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1);
                        grid.Children.Add(label); grid.Children.Add(value);
                    }
                    detailsText = SystemDetails.AsText(rows);
                    Get<TextBlock>("DetailsStatus").Text = L.S("以上信息读取自这台电脑。点“复制全部”可以粘贴到别处。", "以上資訊讀取自這台電腦。按「複製全部」可以貼到別處。", "Read from this PC. “Copy all” puts it on the clipboard.");
                }));
            });
        }

        // ================= Weather =================

        DispatcherTimer weatherTimer;
        int weatherRequest;   // only the newest fetch may update the card (mode can change mid-request)

        void StartWeather()
        {
            WireWeatherSettings();
            if (App.SnapshotMode) { Get<TextBlock>("WeatherText").Text = new WeatherInfo { Place = L.S("示例城市", "Sample City"), Code = 0, Min = 14, Max = 22, Now = 18 }.Text; return; }
            weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            weatherTimer.Tick += (o, e) => RefreshWeather();
            weatherTimer.Start();
            RefreshWeather();
        }

        void RefreshWeather()
        {
            if (App.SnapshotMode) return;
            int id = Interlocked.Increment(ref weatherRequest);
            Get<TextBlock>("WeatherNow").Text = L.S("当前：获取中…", "目前：讀取中…", "Now: loading…");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var w = Weather.Fetch(settings);
                W.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (id != weatherRequest) return;
                    lastWeather = w;
                    Get<TextBlock>("WeatherText").Text = w.Text;
                    Get<TextBlock>("WeatherNow").Text = L.S("当前：", "目前：", "Now: ") + (w.Error ?? w.Text);
                    weatherTimer.Interval = TimeSpan.FromMinutes(w.Error == null ? 30 : 5);   // retry sooner after a failure
                }));
            });
        }

        void WireWeatherSettings()
        {
            var modes = new Dictionary<string, string> { { "location", "WeatherByLocation" }, { "ip", "WeatherByIp" }, { "manual", "WeatherManual" } };
            Action show = () =>
            {
                string m = settings.WeatherMode;
                Get<RadioButton>(modes[m]).IsChecked = true;
                Get<UIElement>("CityPicker").Visibility = m == "manual" ? Visibility.Visible : Visibility.Collapsed;
            };
            foreach (var kv in modes)
            {
                string mode = kv.Key;
                Get<RadioButton>(kv.Value).Click += (o, e) => { settings.WeatherMode = mode; settings.Save(); show(); RefreshWeather(); };
            }

            var query = Get<TextBox>("CityQuery");
            var results = Get<ListBox>("CityResults");
            Action search = () =>
            {
                string q = query.Text.Trim();
                if (q.Length == 0) return;
                Get<TextBlock>("WeatherNow").Text = L.S("搜索中…", "Searching…");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    List<Place> found = null;
                    try { found = Weather.Search(q); } catch (Exception ex) { App.Log(ex); }
                    W.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        results.ItemsSource = found;
                        Get<TextBlock>("WeatherNow").Text = found == null ? L.S("搜索失败，请检查网络后再试。", "Search failed. Check your connection and try again.")
                            : found.Count == 0 ? L.F3("没有找到“{0}”，换个写法试试（如 英文名、加上“区/市”）。", "找不到「{0}」，換個寫法試試（例如英文名稱，或加上「區／市」）。", "Nothing found for “{0}”. Try another spelling (e.g. the local or English name).", q)
                            : L.F("找到 {0} 个地点，点一下选择。", "Found {0} places. Click one to pick it.", found.Count);
                    }));
                });
            };
            Get<Button>("CitySearchBtn").Click += (o, e) => search();
            query.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) search(); };
            results.SelectionChanged += (o, e) =>
            {
                var p = results.SelectedItem as Place;
                if (p == null) return;
                settings.WeatherPlace = p.Name;
                settings.WeatherLat = p.Lat;
                settings.WeatherLon = p.Lon;
                settings.Save();
                show();
                RefreshWeather();
            };
            show();
        }

        // ================= Polling =================

        void RestartPolling()
        {
            int ms = settings.RefreshSec * 1000;
            Get<TextBlock>("IntervalText").Text = L.F3("刷新间隔  {0} 秒", "更新頻率  {0} 秒", "Refresh every {0} s", settings.RefreshSec);
            if (pollTimer == null) pollTimer = new Timer(_ => Poll(), null, 0, ms);
            else pollTimer.Change(0, ms);
        }

        void Poll()
        {
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                var s = new Snapshot();
                if (ecOk) s.Ec = ec.Read();
                s.EcOk = ecOk && s.Ec.CpuTemp >= 0;
                s.EcError = ec.Error;
                if (!s.EcOk) { s.Ec.CpuTemp = SystemInfo.AcpiTemp(); s.TempFromAcpi = s.Ec.CpuTemp >= 0; }

                // GPU: driver-reported (discrete GPUs) > EC sensor > integrated GPU shares the CPU die.
                int gpuFan;
                int drv = Gpu.DriverTemp(out gpuFan);
                if (drv > 0) { s.GpuTemp = drv; s.GpuSource = L.S("显卡驱动读取", "From the GPU driver"); }
                else if (s.EcOk && s.Ec.GpuTemp > 0) { s.GpuTemp = s.Ec.GpuTemp; s.GpuSource = L.S("EC 实时读取", "Live from the EC"); }
                else if (gpuIntegrated && s.Ec.CpuTemp >= 0) { s.GpuTemp = s.Ec.CpuTemp; s.GpuShared = true; s.GpuSource = L.S("集成显卡 · 与 CPU 同一芯片", "Integrated · same chip as the CPU"); }
                else s.GpuSource = gpuIntegrated ? L.S("集成显卡 · 未读取", "Integrated · not read") : L.S("显卡驱动未提供", "Not reported by the driver");
                Gpu.Usage(out s.GpuUsage, out s.VramDedicatedGb, out s.VramSharedGb);
                s.Cpu = SystemInfo.CpuUsage();
                SystemInfo.Memory(out s.MemUsed, out s.MemTotal);
                SystemInfo.Battery(out s.Battery, out s.Plugged, out s.Charging);
                // Battery details and switch states change rarely and are slow to query (SetupAPI, WinRT, WMI):
                // read them about every 5 s, or right after a switch was flipped; otherwise carry the last values.
                bool slow = forceSlowPoll || pollCount++ % Math.Max(1, 5 / settings.RefreshSec) == 0;
                forceSlowPoll = false;
                var prev = last;
                // Battery: read every poll (the device path is cached, so it's cheap).
                s.Bat = ReadBattery(s.EcOk ? s.Ec : null);
                s.Wifi = slow ? Radios.Get(Radios.WiFi) : prev.Wifi;
                s.Bluetooth = slow ? Radios.Get(Radios.Bluetooth) : prev.Bluetooth;
                s.Touchpad = slow ? Devices.Enabled(Devices.TouchpadId) : prev.Touchpad;
                s.Camera = slow ? Devices.Enabled(Devices.CameraId) : prev.Camera;
                s.Mic = slow ? SystemInfo.MicOn() : prev.Mic;
                s.Brightness = SystemInfo.GetBrightness();
                s.Volume = SystemInfo.GetVolume(out s.Muted);
                s.PowerMode = SystemInfo.GetPowerMode();
                s.PowerModeAvailable = SystemInfo.PowerModeAvailable();
                AppEntry[] list = null;
                W.Dispatcher.Invoke(() => list = apps.ToArray());
                foreach (var a in list) s.Running[a] = Launcher.IsRunning(a);
                W.Dispatcher.BeginInvoke(new Action(() => Apply(s)));
            }
            catch (Exception e) { App.Log(e); }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        // On boards whose EC profile maps the smart battery, its live current x voltage gives the power immediately,
        // and its own design / full-charge capacity and cycle count replace the driver's (this HP driver reports
        // full = design). Applied before the times are computed, and used by the details page as well.
        static BatteryReading ReadBattery(EcSnapshot e)
        {
            if (e == null) return Battery.Read();
            int? rateMW = e.BatRateMA != int.MinValue && e.BatVoltMV > 0 ? (int?)((long)e.BatRateMA * e.BatVoltMV / 1000) : null;
            return Battery.Read(rateMW, b =>
            {
                if (e.BatDesignMAh <= 0 || e.BatFullMAh <= 0 || e.BatDesignMV <= 0) return;
                b.DesignMWh = (int)((long)e.BatDesignMAh * e.BatDesignMV / 1000);
                b.FullMWh = (int)((long)e.BatFullMAh * e.BatDesignMV / 1000);
                if (e.BatCycles >= 0) b.CycleCount = e.BatCycles;
            });
        }

        void Apply(Snapshot s)
        {
            last = s;
            var e = s.Ec;

            // Status strip
            // Normally just when the readings were taken; the reason only when fan / temperature can't be read.
            Get<TextBlock>("StatusText").Text = s.EcOk
                ? L.S("数据更新于 ", "資料更新於 ", "Updated ") + DateTime.Now.ToString("HH:mm:ss")
                : profile == null
                    ? L.S("此机型的风扇读取尚未适配", "Fan reading isn't supported on this model") + (s.TempFromAcpi ? L.S("，CPU 温度来自 ACPI 温度区", "; CPU temperature from ACPI") : "")
                    : L.S("风扇与温度暂时读不到", "Fan and temperatures can't be read right now") + (string.IsNullOrEmpty(s.EcError) ? "" : L.S("：", ": ") + s.EcError);

            // Hardware table
            string notRead = L.S("未读取", "Not read");
            string tempSource = s.EcOk ? L.S("EC 实时读取", "Live from the EC") : s.TempFromAcpi ? L.S("ACPI 温度区", "ACPI thermal zone") : notRead;
            string fanMissing = profile == null ? NotSupported : notRead;
            hwCpuTempV.Text = e.CpuTemp >= 0 ? e.CpuTemp + "°C" : "—";
            hwCpuTempS.Text = e.CpuTemp >= settings.AlertTemp ? L.S("偏高", "High") : tempSource;
            hwGpuTempV.Text = s.GpuTemp > 0 ? s.GpuTemp + "°C" : "—";
            hwGpuTempS.Text = s.GpuSource;
            thGpu.Text = hwGpuTempV.Text;
            thGpuNote.Text = s.GpuSource;
            hwCpuV.Text = s.Cpu >= 0 ? s.Cpu + "%" : "—";
            string counter = L.S("系统计数", "系統效能計數器", "Windows counters");
            hwCpuS.Text = counter;
            hwMemV.Text = s.MemUsed.ToString("F1") + " GB";
            hwMemS.Text = L.F("共 {0} GB", "of {0} GB", Math.Round(s.MemTotal));
            hwBoardV.Text = e.BoardTemp > 0 ? e.BoardTemp + "°C" : "—";
            hwBoardS.Text = e.BoardTemp > 0 ? L.S("EC 实时读取", "Live from the EC") : (s.EcOk ? L.S("未提供", "Not available") : fanMissing);
            // Fan speed and state share one row.
            hwFanV.Text = e.FanRpm >= 0 ? e.FanRpm + " RPM" : "—";
            if (!s.EcOk) hwFanS.Text = fanMissing;
            else if (e.FanFault) hwFanS.Text = L.S("异常 · EC 报告风扇故障", "Fault · the EC reports a fan error");
            else if (e.FanRpm > 0) hwFanS.Text = L.S("运转中 · 正常", "Spinning · normal");
            else hwFanS.Text = L.S("停转 · 低温时正常", "Stopped · normal when cool");

            hwGpuV.Text = s.GpuUsage >= 0 ? s.GpuUsage + "%" : "—";
            hwGpuS.Text = s.GpuUsage >= 0 ? counter : notRead;
            bool discrete = s.VramDedicatedGb >= 0.0625;
            if (s.VramSharedGb < 0) { hwVramV.Text = "—"; hwVramS.Text = notRead; }
            else if (discrete) { hwVramV.Text = s.VramDedicatedGb.ToString("F1") + " GB"; hwVramS.Text = vramTotalGb > 0 ? L.F3("共 {0} GB 专用显存", "共 {0} GB 專用顯示記憶體", "of {0} GB dedicated", Math.Round(vramTotalGb)) : L.S("专用显存", "專用顯示記憶體", "Dedicated"); }
            else { hwVramV.Text = s.VramSharedGb.ToString("F1") + " GB"; hwVramS.Text = L.F("共享内存 · 上限 {0} GB", "Shared memory · up to {0} GB", (s.MemTotal / 2).ToString("F1")); }
            hwCpuTempV.Foreground = e.CpuTemp >= settings.AlertTemp ? Brushes.Crimson : new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));

            // Thermal page
            thCpu.Text = hwCpuTempV.Text;
            thBoard.Text = hwBoardV.Text;
            thFan.Text = hwFanV.Text;
            thPassive.Text = e.PassiveTemp > 0 ? e.PassiveTemp + "°C" : "—";
            thCritical.Text = e.CriticalTemp > 0 ? e.CriticalTemp + "°C" : "—";

            // Chart
            var now = DateTime.Now;
            if (e.CpuTemp >= 0) history.Add(new KeyValuePair<DateTime, int>(now, e.CpuTemp));
            history.RemoveAll(p => (now - p.Key).TotalSeconds > 62);
            // A GPU with its own sensor gets its own curve; an integrated GPU is the CPU curve.
            bool gpuOwn = s.GpuTemp > 0 && !s.GpuShared;
            if (gpuOwn) gpuHistory.Add(new KeyValuePair<DateTime, int>(now, s.GpuTemp));
            gpuHistory.RemoveAll(p => (now - p.Key).TotalSeconds > 62);
            Get<TextBlock>("ChartTitle").Text = gpuOwn ? L.S("CPU / GPU 温度 · 最近 60 秒", "CPU / GPU temp · last 60 s")
                : gpuIntegrated ? L.S("CPU / GPU 温度 · 最近 60 秒（核显同温）", "CPU / GPU 溫度 · 最近 60 秒（內顯同溫）", "CPU / GPU temp · last 60 s (shared chip)") : L.S("CPU 温度 · 最近 60 秒", "CPU temp · last 60 s");
            DrawChart();

            // Controls (don't fight the user while they drag)
            suppress = true;
            if (s.Brightness >= 0 && (now - userTouchedBrightness).TotalSeconds > 2) SetBrightnessUi(s.Brightness);
            if (s.Volume >= 0 && (now - userTouchedVolume).TotalSeconds > 2) SetVolumeUi(s.Volume);
            suppress = false;
            string mute = MuteLabel(s.Muted);
            Get<Button>("HomeMuteBtn").Content = mute;
            Get<Button>("PageMuteBtn").Content = mute;
            SetPowerUi(s.PowerMode, s.PowerModeAvailable);

            // Battery
            string batState = s.Battery < 0 ? L.S("没有检测到电池", "No battery") : s.Charging ? L.S("正在充电", "Charging") : s.Plugged ? L.S("已接通电源", "Plugged in") : L.S("使用电池", "On battery");
            string eta = s.Bat.RemainingMin > 0 ? L.S(" · 剩约 ", " · ~") + L.Duration(s.Bat.RemainingMin) + L.S("", " left")
                       : s.Bat.ToFullMin > 0 ? L.S(" · 约 ", " · ~") + L.Duration(s.Bat.ToFullMin) + L.S("充满", " to full") : "";
            Get<TextBlock>("GirlBattery").Text = (s.Battery >= 0 ? L.S("电量 ", "Battery ") + s.Battery + "% · " + batState : batState) + eta;
            ApplyBattery(s);
            UpdateTiles(s);
            Get<TextBlock>("BatteryBig").Text = s.Battery >= 0 ? s.Battery + "%" : "—";
            Get<TextBlock>("BatteryState").Text = batState;
            int pct = Math.Max(0, Math.Min(100, s.Battery));
            Get<ColumnDefinition>("BatteryFill").Width = new GridLength(pct, GridUnitType.Star);
            Get<ColumnDefinition>("BatteryRest").Width = new GridLength(100 - pct, GridUnitType.Star);

            // Apps
            UpdateAppStates(s);

            // HP 娘
            string kind;
            string alert = Mascot.Alert(s, settings.AlertTemp, out kind);
            if (alert != null && kind != currentAlertKind) ShowLine(alert);
            else if (alert != null) { currentLine = alert; Get<TextBlock>("GirlLine").Text = alert; }   // same alert, new number: no re-fade
            else if (currentAlert != null) ShowLine(NextLine(s));
            currentAlert = alert;   // everyday lines rotate on the clock tick (UpdateClock)
            currentAlertKind = kind;
        }

        void ShowLine(string text)
        {
            currentLine = text;
            lineChanged = DateTime.Now;
            var tb = Get<TextBlock>("GirlLine");
            tb.Text = text;
            tb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }

        void UpdateClock()
        {
            var n = DateTime.Now;
            Get<TextBlock>("ClockText").Text = n.ToString("HH:mm");
            Get<TextBlock>("DateText").Text = L.En ? n.ToString("ddd, MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : n.ToString("yyyy / MM / dd");

            // Wait for the first readings (MemTotal is set by every poll) so she doesn't open with "读不到".
            if (last.MemTotal > 0 && currentAlert == null && (n - lineChanged).TotalSeconds >= LineSeconds)
                ShowLine(NextLine(last));
        }

        // ================= Chart =================

        void DrawChart()
        {
            var c = Get<Canvas>("ChartCanvas");
            c.Children.Clear();
            double w = c.ActualWidth, h = c.ActualHeight;
            if (w < 50 || h < 50) return;
            const double left = 30, bottom = 24, top = 18;
            double plotW = w - left - 4, plotH = h - bottom - top;
            int max = 80;
            foreach (var p in history.Concat(gpuHistory)) while (p.Value > max) max += 20;
            var faint = (Brush)W.Resources["FaintBrush"];
            var line = (Brush)W.Resources["LineBrush"];

            c.Children.Add(Label("°C", left, 0, faint));
            for (int v = 0; v <= max; v += 20)
            {
                double y = top + plotH - plotH * v / max;
                c.Children.Add(new Line { X1 = left + 6, X2 = left + plotW, Y1 = y, Y2 = y, Stroke = line, StrokeThickness = 1 });
                var t = Label(v.ToString(), 0, y - 8, faint); t.Width = 22; t.TextAlignment = TextAlignment.Right;
                c.Children.Add(t);
            }
            string[] xs = L.En ? new[] { "60 s ago", "40 s ago", "20 s ago", "now" } : new[] { "60 秒前", "40 秒前", "20 秒前", L.S("现在", "") };
            for (int i = 0; i < 4; i++)
            {
                double x = left + 6 + (plotW - 6) * i / 3.0;
                var t = Label(xs[i], x - (i == 3 ? 26 : i == 0 ? 0 : 20), top + plotH + 6, faint);
                c.Children.Add(t);
            }

            var now = DateTime.Now;
            Func<List<KeyValuePair<DateTime, int>>, Brush, Polyline> curve = (data, brush) =>
            {
                var poly = new Polyline { Stroke = brush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                foreach (var p in data)
                {
                    double age = Math.Min(60, (now - p.Key).TotalSeconds);
                    poly.Points.Add(new Point(left + 6 + (plotW - 6) * (1 - age / 60.0), top + plotH - plotH * p.Value / max));
                }
                return poly;
            };
            c.Children.Add(curve(history, CpuBrush));
            if (gpuHistory.Count > 0) c.Children.Add(curve(gpuHistory, GpuBrush));

            // Legend under the chart, in the curves' colours. A discrete GPU always gets its own entry
            // (even before its first reading); an integrated GPU shares the CPU curve.
            var tail = Get<TextBlock>("ChartTailText");
            tail.Inlines.Clear();
            Action<string, Brush, List<KeyValuePair<DateTime, int>>> entry = (name, brush, data) =>
            {
                if (tail.Inlines.Count > 0) tail.Inlines.Add(new System.Windows.Documents.Run("      "));
                tail.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = brush });
                tail.Inlines.Add(new System.Windows.Documents.Run(name + " " + (data.Count > 0 ? data[data.Count - 1].Value + "°C" : "—")));
            };
            entry(gpuIntegrated ? L.S("CPU / 核显", "CPU / 內顯", "CPU / iGPU") : "CPU", CpuBrush, history);
            if (Gpu.HasDiscrete(machine.Gpu) || gpuHistory.Count > 0) entry(L.S("独立显卡", "獨立顯示卡", "Discrete GPU"), GpuBrush, gpuHistory);
        }

        // Fixed colours (not the theme accent) so the two curves are always told apart: blue CPU, orange GPU.
        static readonly Brush CpuBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));
        static readonly Brush GpuBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x8C, 0x28));

        static TextBlock Label(string text, double x, double y, Brush b)
        {
            var t = new TextBlock { Text = text, FontSize = 11, Foreground = b };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            return t;
        }

        // ================= Brightness / volume / power =================

        void WireControls()
        {
            foreach (var n in new[] { "HomeBrightness", "PageBrightness" })
                Get<Slider>(n).ValueChanged += (o, e) => { if (!suppress) UserBrightness((int)Math.Round(e.NewValue)); };
            foreach (var n in new[] { "HomeVolume", "PageVolume" })
                Get<Slider>(n).ValueChanged += (o, e) => { if (!suppress) UserVolume((int)Math.Round(e.NewValue)); };

            brightnessDebounce.Tick += (o, e) =>
            {
                brightnessDebounce.Stop();
                int v = pendingBrightness;
                ThreadPool.QueueUserWorkItem(_ => SystemInfo.SetBrightness(v));
            };

            foreach (int v in new[] { 25, 50, 75, 100 })
            {
                int val = v;
                var b = new Button { Content = v + "%", Width = 72, Margin = new Thickness(0, 0, 12, 0), Style = (Style)W.Resources["OutlineButton"] };
                b.Click += (o, e) => UserBrightness(val);
                Get<StackPanel>("BrightnessPresets").Children.Add(b);
                var c = new Button { Content = v + "%", Width = 72, Margin = new Thickness(0, 0, 12, 0), Style = (Style)W.Resources["OutlineButton"] };
                c.Click += (o, e) => UserVolume(val);
                Get<StackPanel>("VolumePresets").Children.Add(c);
            }

            RoutedEventHandler mute = (o, e) =>
            {
                bool m = !last.Muted;
                SystemInfo.SetMute(m);
                last.Muted = m;
                Get<Button>("HomeMuteBtn").Content = Get<Button>("PageMuteBtn").Content = MuteLabel(m);
            };
            Get<Button>("HomeMuteBtn").Click += mute;
            Get<Button>("PageMuteBtn").Click += mute;

            var modes = new[] {
                new { Home = "HomePowerSaver", Page = "PagePowerSaver", Mode = SystemInfo.PowerSaver },
                new { Home = "HomePowerBalanced", Page = "PagePowerBalanced", Mode = SystemInfo.PowerBalanced },
                new { Home = "HomePowerPerf", Page = "PagePowerPerf", Mode = SystemInfo.PowerPerformance },
            };
            foreach (var m in modes)
            {
                var mode = m.Mode;
                RoutedEventHandler click = (o, e) => { SystemInfo.SetPowerMode(mode); SetPowerUi(SystemInfo.GetPowerMode(), SystemInfo.PowerModeAvailable()); };
                Get<RadioButton>(m.Home).Click += click;
                Get<RadioButton>(m.Page).Click += click;
            }
        }

        static string MuteLabel(bool muted) { return muted ? L.S("取消静音", "Unmute") : L.S("静音", "Mute"); }

        void UserBrightness(int v)
        {
            userTouchedBrightness = DateTime.Now;
            suppress = true; SetBrightnessUi(v); suppress = false;
            pendingBrightness = v;
            brightnessDebounce.Stop(); brightnessDebounce.Start();
        }

        void UserVolume(int v)
        {
            userTouchedVolume = DateTime.Now;
            suppress = true; SetVolumeUi(v); suppress = false;
            SystemInfo.SetVolume(v);
        }

        void SetBrightnessUi(int v)
        {
            Get<Slider>("HomeBrightness").Value = v; Get<Slider>("PageBrightness").Value = v;
            Get<TextBlock>("HomeBrightnessText").Text = Get<TextBlock>("PageBrightnessText").Text = v + "%";
        }

        void SetVolumeUi(int v)
        {
            Get<Slider>("HomeVolume").Value = v; Get<Slider>("PageVolume").Value = v;
            Get<TextBlock>("HomeVolumeText").Text = Get<TextBlock>("PageVolumeText").Text = v + "%";
        }

        void SetPowerUi(Guid g, bool available)
        {
            bool saver = g == SystemInfo.PowerSaver, perf = g == SystemInfo.PowerPerformance, bal = !saver && !perf;
            Get<RadioButton>("HomePowerSaver").IsChecked = Get<RadioButton>("PagePowerSaver").IsChecked = saver;
            Get<RadioButton>("HomePowerBalanced").IsChecked = Get<RadioButton>("PagePowerBalanced").IsChecked = bal;
            Get<RadioButton>("HomePowerPerf").IsChecked = Get<RadioButton>("PagePowerPerf").IsChecked = perf;
            foreach (var n in new[] { "HomePowerSaver", "PagePowerSaver", "HomePowerBalanced", "PagePowerBalanced", "HomePowerPerf", "PagePowerPerf" })
                Get<RadioButton>(n).IsEnabled = available;
            Get<TextBlock>("PowerModeHint").Text = !available
                ? L.S("当前电源计划不是「平衡」，Windows 只在平衡计划下提供电源模式。要用的话，在右边的电源计划里切回「平衡」。",
                      "Windows only offers power modes on the Balanced plan. Switch the power plan on the right back to Balanced to use them.")
                : saver ? L.S("省电：降低性能，让电池用得更久。", "Saver: lower performance, longer battery life.")
                : perf ? L.S("高性能：CPU 全力运行，风扇可能转得更快、更耗电。", "Performance: the CPU runs flat out; the fan may spin faster and battery drains quicker.")
                : L.S("平衡：日常使用的推荐模式，在性能和续航之间自动取舍。", "Balanced: recommended for everyday use, trades off speed and battery life automatically.");
        }

        // ================= Power plans =================

        // Plans change rarely (and mostly in Windows' own dialog), so the list is re-read when the
        // power page is opened and when the window gets focus back, not on every poll.
        void WirePowerPlans()
        {
            // Control Panel → Power Options
            Get<Button>("PowerOptionsBtn").Click += (o, e) => Launcher.ShellOpen("shell:::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}");
            Get<RadioButton>("NavPower").Checked += (o, e) => RefreshPowerPlans();
            W.Activated += (o, e) => { if (Get<UIElement>("PagePower").Visibility == Visibility.Visible) RefreshPowerPlans(); };
            RefreshPowerPlans();
        }

        void RefreshPowerPlans()
        {
            var host = Get<WrapPanel>("PowerPlans");
            host.Children.Clear();
            Guid active = SystemInfo.ActivePlan();
            foreach (var p in SystemInfo.PowerPlans())
            {
                var plan = p.Item1;
                var rb = new RadioButton { Content = p.Item2, GroupName = "PowerPlan", MinWidth = 96, Style = (Style)W.Resources["SegItem"], IsChecked = plan == active };
                rb.Click += (o, e) =>
                {
                    if (!SystemInfo.SetPlan(plan)) MessageBox.Show(W, L.S("切换电源计划失败。", "Couldn't switch the power plan."), L.T("电源计划"));
                    RefreshPowerPlans();
                    SetPowerUi(SystemInfo.GetPowerMode(), SystemInfo.PowerModeAvailable());
                };
                host.Children.Add(rb);
            }
        }

        // ================= System actions =================

        void WireSystemActions()
        {
            Get<Button>("LockBtn").Click += (o, e) => SystemInfo.Lock();
            Get<Button>("SleepBtn").Click += (o, e) => SystemInfo.Sleep();
            Get<Button>("RestartBtn").Click += (o, e) =>
            {
                if (MessageBox.Show(W, L.S("确定要重启电脑吗？\n请先保存正在编辑的文件。", "確定要重新啟動電腦嗎？\n請先儲存正在編輯的檔案。", "Restart the PC now?\nSave any open work first."), L.T("重启"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                    SystemInfo.Restart();
            };
            Get<Button>("ShutdownBtn").Click += (o, e) =>
            {
                if (MessageBox.Show(W, L.S("确定要关机吗？\n请先保存正在编辑的文件。", "確定要關機嗎？\n請先儲存正在編輯的檔案。", "Shut down the PC now?\nSave any open work first."), L.T("关机"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                    SystemInfo.Shutdown();
            };
        }

        // ================= Quick launch =================

        void LoadApps()
        {
            apps = Launcher.BuiltIn();
            foreach (var p in settings.CustomApps) apps.Add(Launcher.FromPath(p));
            Get<Button>("AddAppBtn").Click += (o, e) => AddApp();
            RebuildApps();
        }

        void AddApp()
        {
            var dlg = new OpenFileDialog { Title = L.S("选择要添加的程序", "Choose a program to add"), Filter = L.S("程序或快捷方式|*.exe;*.lnk|所有文件|*.*", "程式或捷徑|*.exe;*.lnk|所有檔案|*.*", "Programs or shortcuts|*.exe;*.lnk|All files|*.*") };
            if (dlg.ShowDialog(W) != true) return;
            if (settings.CustomApps.Contains(dlg.FileName)) return;
            settings.CustomApps.Add(dlg.FileName);
            settings.Save();
            apps.Add(Launcher.FromPath(dlg.FileName));
            RebuildApps();
        }

        void RemoveApp(AppEntry a)
        {
            settings.CustomApps.Remove(a.Target);
            settings.Save();
            apps.Remove(a);
            RebuildApps();
        }

        void ToggleApp(AppEntry a)
        {
            bool running;
            if (last.Running.TryGetValue(a, out running) && running) Launcher.CloseApp(a);
            else Launcher.Open(a);
            // Re-check shortly after the program has had time to start or close.
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += (o, e) => { t.Stop(); ThreadPool.QueueUserWorkItem(_ => Poll()); };
            t.Start();
        }

        readonly Dictionary<AppEntry, Tuple<TextBlock, Button>> pageRows = new Dictionary<AppEntry, Tuple<TextBlock, Button>>();

        void RebuildApps()
        {
            var rows = Get<StackPanel>("AppRows");
            rows.Children.Clear(); pageRows.Clear();
            for (int i = 0; i < apps.Count; i++)
            {
                var a = apps[i];
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = a.Name, FontSize = 15 });
                info.Children.Add(new TextBlock { Text = a.Custom ? a.Target : (a.Close == CloseKind.ExplorerWindows ? L.S("文件资源管理器", "File Explorer") : a.Target), FontSize = 12, Foreground = (Brush)W.Resources["FaintBrush"], TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
                g.Children.Add(info);
                var status = new TextBlock { Text = L.S("未运行", "Not running"), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)W.Resources["SubBrush"] };
                Grid.SetColumn(status, 1); g.Children.Add(status);
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var toggle = new Button { Content = L.S("打开", "Open"), Width = 88, Style = (Style)W.Resources["OutlineButton"] };
                toggle.Click += (o, e) => ToggleApp(a);
                btns.Children.Add(toggle);
                if (a.Custom)
                {
                    var rm = new Button { Content = L.S("移除", "Remove"), Width = 72, Margin = new Thickness(10, 0, 0, 0), Style = (Style)W.Resources["OutlineButton"] };
                    rm.Click += (o, e) => RemoveApp(a);
                    btns.Children.Add(rm);
                }
                Grid.SetColumn(btns, 2); g.Children.Add(btns);
                rows.Children.Add(new Border { Child = g, Padding = new Thickness(0, 12, 0, 12), BorderBrush = (Brush)W.Resources["RowLineBrush"], BorderThickness = new Thickness(0, 0, 0, i == apps.Count - 1 ? 0 : 1) });
                pageRows[a] = Tuple.Create(status, toggle);
            }
            UpdateAppStates(last);
        }

        void UpdateAppStates(Snapshot s)
        {
            var green = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x55));
            foreach (var a in apps)
            {
                bool r;
                s.Running.TryGetValue(a, out r);
                Tuple<TextBlock, Button> row;
                if (pageRows.TryGetValue(a, out row))
                {
                    row.Item1.Text = r ? L.S("● 运行中", "● 執行中", "● Running") : L.S("未运行", "未執行", "Not running");
                    row.Item1.Foreground = r ? green : (Brush)W.Resources["SubBrush"];
                    row.Item2.Content = r ? L.S("关闭", "Close") : L.S("打开", "Open");
                }
            }
        }

        // ================= HP 娘 =================

        void LoadMascotImage()
        {
            BitmapSource img = null;
            if (Settings.IsOwnImage(settings.MascotImage) && File.Exists(settings.MascotImage)) img = LoadFileImage(settings.MascotImage);
            bool custom = img != null;
            if (!custom) img = LoadResourceImage("girl-home.png");
            Get<Image>("GirlImage").Source = img;
            Get<Image>("AvatarPreview").Source = custom ? img : LoadResourceImage("girl-avatar.png");
            Get<TextBlock>("MascotPathText").Text = custom ? Path.GetFileName(settings.MascotImage) : L.T("默认：HP 娘");
        }

        void StartAnimations()
        {
            var bob = new DoubleAnimation(0, -5, TimeSpan.FromSeconds(2.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            var girl = Get<Image>("GirlImage");
            ((TranslateTransform)girl.RenderTransform).BeginAnimation(TranslateTransform.YProperty, bob);

            // Click her for another line; an active warning (heat, low battery) is repeated instead of skipped.
            girl.Cursor = System.Windows.Input.Cursors.Hand;
            girl.ToolTip = L.S("点我换一句", "點我換一句", "Click me for another line");
            girl.MouseLeftButtonUp += (o, e) => ShowLine(currentAlert ?? NextLine(last));
        }

        readonly Queue<string> recentLines = new Queue<string>();   // template keys of the last lines shown
        WeatherInfo lastWeather;

        List<string> hardwareJokes;   // built once from this machine's GPU / CPU names

        string NextLine(Snapshot s)
        {
            if (hardwareJokes == null) hardwareJokes = Jokes.For(machine.Gpu, machine.Cpu);
            return Mascot.Idle(s, biosVersion, hardwareJokes, Jokes.HighPower(machine.Gpu, machine.Cpu), Jokes.Office(machine.Gpu, machine.Cpu), lastWeather, recentLines);
        }

        // ================= Settings page =================

        void WireSettingsPage()
        {
            Get<Button>("ChooseImageBtn").Click += (o, e) =>
            {
                var dlg = new OpenFileDialog { Title = L.S("选择人物图片（推荐透明背景 PNG）", "選擇角色圖片（建議使用透明背景 PNG）", "Choose a character image (transparent PNG recommended)"), Filter = L.S("图片", "Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
                if (dlg.ShowDialog(W) != true) return;
                try
                {
                    // Keep a copy in the data folder so the original can be moved or deleted.
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    if (!Settings.ImageExtensions.Contains(ext)) throw new IOException(L.S("只支持 PNG / JPG / BMP / GIF 图片。", "Only PNG / JPG / BMP / GIF images are supported."));
                    if (new FileInfo(dlg.FileName).Length > Settings.MaxImageBytes) throw new IOException(L.S("图片太大了（最大 20 MB）。", "圖片太大了（最大 20 MB）。", "The image is too large (20 MB max)."));
                    string dest = Path.Combine(Settings.DataDir, "custom-mascot" + ext);
                    if (!SafeFile.WriteAllBytes(dest, File.ReadAllBytes(dlg.FileName)))
                        throw new IOException(L.S("数据文件夹 %APPDATA%\\HPControlPanel 无法安全写入。", "The data folder %APPDATA%\\HPControlPanel can't be written safely."));
                    settings.MascotImage = dest;
                    settings.Save();
                    LoadMascotImage();
                }
                catch (Exception ex) { MessageBox.Show(W, L.S("图片复制失败：", "Couldn't copy the image: ") + ex.Message, L.T("人物图片")); }
            };
            Get<Button>("ResetImageBtn").Click += (o, e) => { settings.MascotImage = ""; settings.Save(); LoadMascotImage(); };

            Get<RadioButton>("Theme" + char.ToUpper(settings.Theme[0]) + settings.Theme.Substring(1)).IsChecked = true;
            foreach (var t in new[] { "blue", "pink", "purple" })
            {
                var theme = t;
                Get<RadioButton>("Theme" + char.ToUpper(t[0]) + t.Substring(1)).Click += (o, e) => { ApplyTheme(theme); settings.Save(); };
            }

            var refresh = new Dictionary<int, string> { { 1, "Refresh1" }, { 2, "Refresh2" }, { 5, "Refresh5" } };
            string current;
            if (!refresh.TryGetValue(settings.RefreshSec, out current)) { settings.RefreshSec = 1; current = "Refresh1"; }
            Get<RadioButton>(current).IsChecked = true;
            foreach (var kv in refresh)
            {
                int sec = kv.Key;
                Get<RadioButton>(kv.Value).Click += (o, e) => { settings.RefreshSec = sec; settings.Save(); RestartPolling(); };
            }

            var alert = Get<Slider>("AlertSlider");
            alert.Value = settings.AlertTemp;
            Get<TextBlock>("AlertText").Text = settings.AlertTemp + "°C";
            alert.ValueChanged += (o, e) =>
            {
                settings.AlertTemp = (int)Math.Round(e.NewValue);
                Get<TextBlock>("AlertText").Text = settings.AlertTemp + "°C";
            };
            alert.PreviewMouseUp += (o, e) => settings.Save();
            alert.LostFocus += (o, e) => settings.Save();

            var auto = Get<CheckBox>("AutoStartSwitch");
            auto.IsChecked = Settings.AutoStartEnabled();
            if (!Settings.InstalledCopy && auto.IsChecked != true)
            {
                // Portable copy: only an installed copy may start elevated at sign-in (see Settings.InstalledCopy).
                auto.IsEnabled = false;
                Get<TextBlock>("AutoStartHint").Text = L.S("免安装运行时不可用，先用 Install.cmd 安装", "免安裝執行時無法使用，請先用 Install.cmd 安裝", "Not available for the portable copy; install with Install.cmd first");
            }
            auto.Click += (o, e) =>
            {
                bool want = auto.IsChecked == true;
                if (!Settings.SetAutoStart(want))
                {
                    auto.IsChecked = !want;
                    MessageBox.Show(W, want && !Settings.InstalledCopy
                        ? L.S("免安装运行时不能开机自启，请先用 Install.cmd 安装。", "免安裝執行時無法開機自動啟動，請先用 Install.cmd 安裝。", "The portable copy can't start with Windows. Install it with Install.cmd first.")
                        : L.S("设置开机自动启动失败。", "Couldn't change the startup setting."), L.T("开机自动启动"));
                }
            };

            var top = Get<CheckBox>("TopMostSwitch");
            top.IsChecked = settings.TopMost;
            top.Click += (o, e) => { settings.TopMost = top.IsChecked == true; W.Topmost = settings.TopMost; settings.Save(); };
        }

        // Every text is chosen when the window is built, so a new language takes effect through a quick restart.
        // The new process is started from this elevated one, so there is no UAC prompt.
        void WireLanguage()
        {
            var map = new Dictionary<string, string> { { "zh-CN", "LangZhCN" }, { "zh-TW", "LangZhTW" }, { "en", "LangEn" } };
            Get<RadioButton>(map[L.Lang]).IsChecked = true;
            foreach (var kv in map)
            {
                string lang = kv.Key;
                Get<RadioButton>(kv.Value).Click += (o, e) =>
                {
                    if (lang == L.Lang) return;
                    settings.Language = lang;
                    settings.Save();
                    if (App.SnapshotMode) return;
                    try
                    {
                        Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--restart NavSettings") { UseShellExecute = false });
                        Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        App.Log(ex);
                        MessageBox.Show(W, L.S("自动重启失败，请手动关闭后重新打开控制台。", "Couldn't restart automatically. Please close and reopen the app."), L.T("语言"));
                    }
                };
            }
        }
    }
}
