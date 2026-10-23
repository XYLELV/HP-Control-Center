// HP 娘's hardware jokes, picked by the GPU and CPU this machine actually has. Each language uses its own
// internet culture: 简体 贴吧 / DIY 圈, 繁體 PTT / 港台玩家用語, English Reddit / PC-gaming memes.
// Tiers follow the GPUs HP has shipped in laptops: Intel UHD / Iris Xe, AMD Vega / 680M / 780M,
// NVIDIA MX, GTX 10/16, RTX 20 – 50 (Victus / OMEN), AMD RX, Intel Arc, and ZBook workstation cards.
// {gpu} / {cpu} are replaced with the real model names.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HPControlPanel
{
    public static class Jokes
    {
        static bool Is(string s, string pattern) { return s != null && Regex.IsMatch(s, pattern, RegexOptions.IgnoreCase); }

        // Simplified, Traditional, English.
        static string J(string cn, string tw, string en) { return L.S(cn, tw, en); }

        public static List<string> For(string gpuList, string cpu)
        {
            var lines = new List<string>();
            var gpus = (gpuList ?? "").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim()).ToList();
            // Discrete only: "Intel Arc Graphics" (Core Ultra) and "Radeon RX Vega" are integrated.
            var dgpu = gpus.FirstOrDefault(g => Is(g, @"NVIDIA|GeForce|Quadro|\bRTX\b|Radeon\s+RX\s*\d{4}|Radeon\s+Pro|\bArc\s+A\d{3}"));
            var igpu = gpus.FirstOrDefault(g => g != dgpu);

            if (dgpu != null) lines.AddRange(Discrete(dgpu));
            else if (igpu != null) lines.AddRange(Integrated(igpu));
            if (dgpu != null && igpu != null)
                lines.Add(Short(igpu) + " + " + Short(dgpu) + J("：核显负责省电，独显负责起飞。", "：內顯負責省電，獨顯負責起飛。", ": the iGPU saves power, the dGPU sends it."));

            if (!string.IsNullOrEmpty(cpu)) lines.AddRange(Cpu(cpu));
            if (HighPower(gpuList, cpu))
            {
                // DIY-circle teasing for power-hungry gaming laptops.
                lines.Add(J("宝宝，续航好短哟～{cpu} 配 {gpu}，离开插座就开始慌了吧？", "寶寶，續航好短喔～{cpu} 配 {gpu}，離開插座就開始慌了吧？",
                    "Baby, your battery life is sooo short~ {cpu} + {gpu}, panicking the second you leave the outlet?"));
                lines.Add(J("插电猛如虎，拔电二百五，说的就是你吧宝宝？", "插電一條龍，拔電一條蟲，說的就是你吧寶寶？",
                    "Plugged in: beast mode. Unplugged: potato mode. That's you, baby."));
                lines.Add(J("宝宝出门记得带上那块砖头适配器，它才是这台电脑的本体。", "寶寶出門記得帶那顆磚頭變壓器，它才是這台電腦的本體。",
                    "Don't forget the power brick, baby. Honestly, it's the main character."));
                lines.Add(J("这配置的续航：一集电视剧都看不完，宝宝要争气呀。", "這配置的續航：一集劇都追不完，寶寶要爭氣啊。",
                    "Battery life of this build: not even one episode. Do better, baby."));
                lines.Add(J("宝宝的电源适配器，比别人的饭盒还大～", "寶寶的變壓器，比別人的便當還大～",
                    "Baby's charger is bigger than other people's lunchboxes~"));
                lines.Add(J("功耗墙拉满的时候，宝宝的电池在偷偷哭。", "功耗牆拉滿的時候，寶寶的電池在偷偷哭。",
                    "When the power limit maxes out, baby's battery cries in silence."));
                // 雌小鬼-style teasing (杂鱼～), always about battery and performance.
                lines.Add(J("宝宝，这就不行了？杂鱼～杂鱼～", "寶寶，這樣就不行了？雜魚～雜魚～♡", "Baby, tapping out already? Zako~ zako~ ♡"));
                lines.Add(J("宝宝续航 30 分钟也已经很棒了哦～摸摸头。", "寶寶續航 30 分鐘也已經很棒了喔～摸摸頭。", "30 minutes of battery is still really good for you, baby~ *head pat*"));
                lines.Add(J("宝宝那么卡还玩得下去，好棒好棒～", "寶寶這麼 lag 還玩得下去，好棒好棒～", "Baby's lagging this hard and still playing? So brave~ good job~"));
                lines.Add(J("就这？高画质一开就喘不过气了，杂鱼显卡～", "就這樣？高畫質一開就喘不過氣，雜魚顯卡～", "That's it? Out of breath on High settings already? Zako GPU~"));
                lines.Add(J("满载五分钟就降频，宝宝的体力也太差了吧～杂鱼～", "滿載五分鐘就降頻，寶寶體力也太差了吧～雜魚～", "Five minutes at full load and you're throttling? Zero stamina, baby~ zako~"));
                lines.Add(J("宝宝别逞强啦，插上电再继续嘛～", "寶寶不要逞強啦，插上電再繼續嘛～", "Don't push yourself, baby. Plug in first, then keep going~"));
                lines.Add(J("风扇叫得这么大声，宝宝是不是太努力了？", "風扇叫這麼大聲，寶寶是不是太努力了？", "Fans screaming like that... baby, you're trying way too hard."));
                lines.Add(J("{gpu} 的宝宝，帧数掉成这样还说自己是游戏本？杂鱼～", "{gpu} 的寶寶，幀數掉成這樣還敢說自己是電競筆電？雜魚～",
                    "A {gpu} baby dropping frames like this and still calling yourself a gaming laptop? Zako~"));
                if (L.En) lines.Add("Bro thinks he's a desktop. Not on battery, bro.");
                if (L.Tw) lines.Add("電競筆電離開插座：效能直接打對折，傻眼貓咪。");
                if (Is(cpu, @"Ryzen")) lines.Add(J("AMD YES！但电池说 NO。", "AMD YES！但電池說 NO。", "AMD YES! The battery says NO."));
                if (Is(cpu, @"\bi9-|Ultra 9") && Is(dgpu, @"RTX\s*\d0[89]0"))
                    lines.Add(J("核弹厂显卡 + 顶级 CPU，宝宝这是要玩游戏还是要发电？", "核彈廠顯卡 + 頂級 CPU，寶寶這是要玩遊戲還是要發電？",
                        "Flagship GPU + top-tier CPU. Baby, are we gaming or running a power plant?"));
            }
            return lines.Select(l => l.Replace("{gpu}", Short(dgpu ?? igpu ?? J("显卡", "顯示卡", "GPU"))).Replace("{cpu}", cpu ?? "")).ToList();
        }

        // Office / thin-and-light class: not a gaming laptop and no gaming GPU (MX still counts as office).
        // Whether there is a battery at all is checked at runtime (desktops have none).
        public static bool Office(string gpuList, string cpu)
        {
            if (HighPower(gpuList, cpu)) return false;
            return !(gpuList ?? "").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries)
                .Any(g => Is(g, @"GeForce\s+(RTX|GTX)|\bRTX\s*\d{4}|Radeon\s+RX\s*\d{4}|\bArc\s+A\d{3}"));
        }

        // Gaming-laptop class: a high-end H/HX CPU together with a discrete GPU.
        public static bool HighPower(string gpuList, string cpu)
        {
            if (string.IsNullOrEmpty(cpu)) return false;
            // i7/i9 H/HX, Core Ultra 7/9 H/HX, Ryzen 7/9 H/HS/HX (incl. "Ryzen AI 9 HX 370" naming)
            bool hotCpu = Is(cpu, @"\bi[79]-\d{4,5}HX?\b|Core\s+Ultra\s+[79]\s+\d{3}HX?\b|Ryzen\s+(9|7)\s+(PRO\s+)?\d{3,4}\w*?(H|HS|HX)\b|Ryzen\s+AI\s+(9|7)\s+(HX\s+)?\d{3}");
            bool dgpu = (gpuList ?? "").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries)
                .Any(g => Is(g, @"GeForce\s+(RTX|GTX)|\bRTX\s*\d{4}|Radeon\s+RX\s*\d{4}"));
            return hotCpu && dgpu;
        }

        static string Short(string gpu) { return Regex.Replace(gpu, @"^(NVIDIA|AMD|Intel)\s+", "", RegexOptions.IgnoreCase).Replace(" Laptop GPU", "").Trim(); }

        // ---------------- Integrated graphics ----------------
        static IEnumerable<string> Integrated(string g)
        {
            if (Is(g, @"Iris\s*Xe"))
                return new[] {
                    J("{gpu}：核显里的优等生，偶尔还能打打网游。", "{gpu}：內顯裡的資優生，偶爾還能打打線上遊戲。", "{gpu}: top of the iGPU class. Can even handle some online games."),
                    J("Iris Xe 跑游戏：低画质 30 帧，这就是核显的浪漫。", "Iris Xe 跑遊戲：低畫質 30 幀，這就是內顯的浪漫。", "Iris Xe gaming: 30 FPS on Low. Living the iGPU dream."),
                    J("{gpu}：别看是核显，剪个 1080p 视频也不在话下。", "{gpu}：別看它是內顯，剪個 1080p 影片沒問題。", "{gpu}: integrated, sure, but 1080p video editing? No problem."),
                    J("核显的尽头是 Iris Xe，Iris Xe 的尽头是……外接显卡坞。", "內顯的盡頭是 Iris Xe，Iris Xe 的盡頭是……外接顯卡盒。", "The end of iGPUs is Iris Xe. The end of Iris Xe is... an eGPU dock."),
                };
            if (Is(g, @"\bArc\b"))
                return new[] {
                    J("{gpu}：Core Ultra 的核显，Intel 核显终于有点样子了。", "{gpu}：Core Ultra 的內顯，Intel 內顯終於像樣了。", "{gpu}: Core Ultra's iGPU. Intel graphics finally look the part."),
                    J("Arc 核显：比 UHD 强一大截，牙膏这次挤得挺多。", "Arc 內顯：比 UHD 強一大截，牙膏這次擠得挺多。", "Arc iGPU: miles ahead of UHD. Intel actually tried this time."),
                    J("{gpu}：网游轻松拿下，3A 低画质也能试试。", "{gpu}：線上遊戲輕鬆跑，3A 低畫質也可以試試。", "{gpu}: esports titles are easy, AAA on Low is worth a shot."),
                };
            if (Is(g, @"(680|780|880|890)M"))
                return new[] {
                    J("{gpu}：最强核显之一，把入门独显按在地上摩擦。", "{gpu}：最強內顯之一，把入門獨顯按在地上磨。", "{gpu}: one of the strongest iGPUs. Bullies entry-level dGPUs for fun."),
                    J("AMD YES！{gpu} 让 MX 系列直接失业。", "AMD YES！{gpu} 讓 MX 系列直接失業。", "AMD YES! {gpu} made the whole MX series unemployed."),
                    J("有 {gpu} 还要什么 MX？核显都能玩 3A 低画质了。", "有 {gpu} 還要什麼 MX？內顯都能跑 3A 低畫質了。", "Who needs MX when you have {gpu}? AAA on Low, integrated."),
                };
            if (Is(g, @"Radeon|Vega"))
                return new[] {
                    J("{gpu}：AMD 核显，比隔壁 UHD 能打一点点。", "{gpu}：AMD 內顯，比隔壁 UHD 強那麼一點點。", "{gpu}: AMD integrated. A little scrappier than Intel UHD."),
                    J("Vega 核显：当年的核显战神，现在的养老选手。", "Vega 內顯：當年的內顯戰神，現在的退休選手。", "Vega iGPU: once the king of integrated, now enjoying retirement."),
                    J("AMD 核显 + 双通道内存 = 白嫖的性能，单通道就当没说。", "AMD 內顯 + 雙通道記憶體 = 免費的效能；單通道就當我沒說。", "AMD iGPU + dual-channel RAM = free performance. Single-channel? Pretend I said nothing."),
                };
            // Intel UHD / HD
            var l = new List<string> {
                J("检测到集成显卡。温馨提示：建议拔掉笔记本，换一台有独显的，性能立刻提升 300%！", "偵測到內顯。溫馨提醒：建議換一台有獨顯的筆電，效能馬上提升 300%！",
                  "Integrated graphics detected. Pro tip: replace laptop with one that has a real GPU. Instant +300% performance!"),
                J("{gpu}：我不是显卡，我只是 CPU 顺便兼职的。", "{gpu}：我不是顯卡，我只是 CPU 順便兼差的。", "{gpu}: I'm not a GPU, I'm just the CPU's side hustle."),
                J("想玩 3A 大作？可以先打开写字板，用文字想象一下画面。", "想玩 3A 大作？可以先打開 WordPad，用文字想像一下畫面。", "Want to play AAA games? Open WordPad and imagine the graphics in text."),
                J("独显：在吗？核显：在 CPU 里。", "獨顯：在嗎？內顯：在 CPU 裡。", "dGPU: u up? iGPU: I'm inside the CPU."),
                J("核显打游戏，就像用勺子挖隧道——能挖，就是慢。", "內顯打遊戲，就像用湯匙挖隧道——挖得動，就是慢。", "Gaming on an iGPU is like digging a tunnel with a spoon. Possible. Slow."),
                J("已为你自动开启「PPT 画质」，每一帧都值得细细品味。", "已為你自動開啟「投影片畫質」，每一幀都值得細細品味。", "Slideshow mode enabled automatically. Savor every single frame."),
                J("听说插上一块 5090 就能起飞，可惜这台笔记本没有地方插。", "聽說插上一張 5090 就能起飛，可惜這台筆電沒地方插。", "Heard a 5090 would fix everything. Sadly there's nowhere to plug it in."),
                J("{gpu} 也是显卡！只是……不太爱打游戏。", "{gpu} 也是顯卡！只是……不太愛打遊戲。", "{gpu} is a GPU too! It just... doesn't really game."),
                J("帧数不够？试试把亮度调高，看起来会更流畅（并不会）。", "幀數不夠？把亮度調高試試，看起來會比較順（並不會）。", "Low FPS? Try turning up the brightness. Feels smoother. (It doesn't.)"),
                J("核显最大的优点：从来不会因为显卡太贵而心疼。", "內顯最大的優點：永遠不會因為顯卡太貴而心痛。", "Best thing about integrated graphics: GPU prices can't hurt you."),
                J("建议开启省电模式，反正大型游戏也跑不动（狗头）。", "建議開省電模式，反正大型遊戲也跑不動（狗頭）。", "Might as well use Power saver. The big games won't run anyway. (jk... mostly)"),
                J("本机显卡性能评级：能亮。", "本機顯卡效能評級：會亮。", "GPU performance rating: it turns on."),
            };
            if (L.En)
            {
                l.Add("But can it run Crysis? ...No. No it cannot.");
                l.Add("Certified potato PC. 🥔 Respectfully.");
                l.Add("Download more VRAM? There isn't any to begin with.");
                l.Add("Minecraft on Fast graphics, 8 chunks. Living the dream.");
            }
            if (L.Tw)
            {
                l.Add("顯卡等級：文書機認證，打字超順。");
                l.Add("想跑 3A？先問問 CPU 肯不肯，它已經身兼兩職了。");
            }
            return l;
        }

        // ---------------- Discrete graphics ----------------
        static IEnumerable<string> Discrete(string g)
        {
            var l = new List<string>();
            var rtx = Regex.Match(g, @"RTX\s*(\d{4})", RegexOptions.IgnoreCase);
            int model = rtx.Success ? int.Parse(rtx.Groups[1].Value) : 0;
            int tier = model % 100;   // 50 / 60 / 70 / 80 / 90

            if (Is(g, @"\bMX\s*\d{3}"))
            {
                l.Add(J("{gpu}：主打一个“我也有独显”，实际性能和核显五五开。", "{gpu}：主打一個「我也有獨顯」，實際效能跟內顯五五波。", "{gpu}: technically a dedicated GPU. Practically a coin flip vs integrated."));
                l.Add(J("MX 系列：贴吧认证亮机卡，亮得很稳定。", "MX 系列：鄉民認證亮機卡，亮得很穩。", "MX series: certified display adapter. Displays things. Reliably."));
                l.Add(J("{gpu} 跑分出来的那一刻，隔壁 Iris Xe 笑出了声。", "{gpu} 跑分出來那一刻，隔壁 Iris Xe 笑出聲。", "The moment {gpu} finished its benchmark, Iris Xe laughed out loud."));
                l.Add(J("买的时候：独显笔记本！用的时候：独显笔记本？", "買的時候：獨顯筆電！用的時候：獨顯筆電？", "When buying: dedicated GPU laptop! When using: dedicated GPU... laptop?"));
                l.Add(J("{gpu}：能带动 LOL，就是我最大的倔强。", "{gpu}：跑得動 LOL，就是我最後的倔強。", "{gpu}: runs League of Legends. That's its whole personality."));
                if (Is(g, @"MX\s*1[1-5]0")) l.Add(J("MX150：当年的轻薄本神卡，如今的亮机卡天花板。", "MX150：當年的輕薄筆電神卡，現在的亮機卡天花板。", "MX150: thin-and-light legend back then, glorified display adapter now."));
                if (Is(g, @"MX\s*(2[0-9]0|3[0-9]0)")) l.Add(J("{gpu}：换了个名字，还是熟悉的 MX 味道。", "{gpu}：換了名字，還是熟悉的 MX 味道。", "{gpu}: new name, same old MX flavor."));
                if (Is(g, @"MX\s*(4|5)[0-9]0")) l.Add(J("{gpu}：MX 家族的天花板，也就是入门独显的地板。", "{gpu}：MX 家族的天花板，也就是入門獨顯的地板。", "{gpu}: the ceiling of the MX family is the floor of real GPUs."));
                if (L.En) l.Add("{gpu} trying to run Cyberpunk: 'I'm in danger.'");
            }
            else if (Is(g, @"Quadro|RTX\s*A\d{3,4}|RTX\s*\d{4}\s*Ada|Radeon\s+Pro"))
            {
                l.Add(J("{gpu}：专业卡，做图渲染稳如老狗，打游戏……别问。", "{gpu}：專業卡，算圖穩如泰山，打遊戲……別問。", "{gpu}: pro card. Rock-solid for rendering. Gaming? Don't ask."));
                l.Add(J("ZBook 配专业卡：老板花钱买的稳定，你用来看视频。", "ZBook 配專業卡：老闆花錢買穩定，你拿來看影片。", "ZBook with a pro GPU: the company paid for stability, you use it for YouTube."));
                l.Add(J("{gpu} 驱动认证一大堆，就是没认证过《原神》。", "{gpu} 一堆驅動認證，就是沒認證過《原神》。", "{gpu}: certified for every pro app except Genshin Impact."));
            }
            else if (Is(g, @"GTX\s*10[5-8]0"))
            {
                l.Add(J("{gpu}：老将不死，只是慢慢凋零。", "{gpu}：老兵不死，只是逐漸凋零。", "{gpu}: old soldiers never die, they just fade away."));
                l.Add(J("Pascal 架构：当年的神，现在的钉子户。", "Pascal 架構：當年的神，現在的釘子戶。", "Pascal: a god back then, still refusing to retire."));
                if (Is(g, @"1050")) l.Add(J("1050 Ti 钉子户：网吧标配，传奇不朽。", "1050 Ti：網咖標配，傳奇不朽。", "1050 Ti: internet café legend. Never dies."));
                if (Is(g, @"10[78]0")) l.Add(J("{gpu}：当年的游戏本旗舰，现在也还能打。", "{gpu}：當年的電競筆電旗艦，現在也還能打。", "{gpu}: gaming laptop flagship back in the day, still fights."));
            }
            else if (Is(g, @"GTX\s*16[56]0"))
            {
                l.Add(J("{gpu}：没有光追，但有一颗不服老的心。", "{gpu}：沒有光追，但有一顆不服老的心。", "{gpu}: no ray tracing, but a heart that refuses to age."));
                l.Add(J("16 系：RTX 买不起时的最佳选择，钉子户 2.0。", "16 系列：買不起 RTX 時的最佳選擇，釘子戶 2.0。", "GTX 16: the 'RTX is too expensive' pick. Still holding on."));
                l.Add(J("GTX 1650：网游全开，3A 随缘，主打一个够用。", "GTX 1650：線上遊戲全開，3A 隨緣，主打夠用。", "GTX 1650: esports maxed, AAA is vibes-based. It's enough."));
            }
            else if (model > 0)
            {
                if (tier == 50)
                {
                    l.Add(J("{gpu}：入门光追，开了光追就入门 PPT。", "{gpu}：入門光追，開了光追就入門投影片。", "{gpu}: entry-level ray tracing. Turn it on for an entry-level slideshow."));
                    l.Add(J("甜品卡？对显存来说是代糖卡。", "甜品卡？以顯存來說是代糖卡。", "Budget card? With that VRAM it's more of a diet card."));
                    l.Add(J("{gpu}：能玩，但别问开什么画质。", "{gpu}：能玩，但不要問開什麼畫質。", "{gpu}: it runs. Don't ask about settings."));
                    if (Is(g, @"3050")) l.Add(J("3050 4G：显存比我的耐心还少。", "3050 4G：顯存比我的耐心還少。", "3050 with 4 GB: less VRAM than I have patience."));
                    if (Is(g, @"2050")) l.Add(J("RTX 2050：名字里有 RTX，心里是 MX。", "RTX 2050：名字有 RTX，骨子裡是 MX。", "RTX 2050: RTX on the box, MX in its soul."));
                }
                else if (tier == 60)
                {
                    l.Add(J("{gpu}：游戏本主流神卡，性价比之选。", "{gpu}：電競筆電主流神卡，CP 值首選。", "{gpu}: the people's GPU. Best bang for the buck."));
                    l.Add(J("x060 定律：玩家最多，吐槽最多，买的也最多。", "x060 定律：玩家最多、抱怨最多、買的人也最多。", "The x060 law: most players, most complaints, most sales."));
                    if (model >= 4060) l.Add(J("{gpu} 8G：老黄刀法精准，懂的都懂。", "{gpu} 8G：老黃刀工精準，懂的都懂。", "{gpu} with 8 GB: the more you buy, the more you save. — Jensen, probably"));
                    else l.Add(J("{gpu}：当年的甜品，现在依然能打。", "{gpu}：當年的甜品卡，現在依然能打。", "{gpu}: yesterday's sweet spot, still fighting today."));
                }
                else if (tier == 70)
                {
                    l.Add(J("{gpu}：高端玩家入场券，2K 高画质稳稳的。", "{gpu}：高階玩家入場券，2K 高畫質穩穩的。", "{gpu}: your ticket to high-end. 1440p High, no sweat."));
                    l.Add(J("{gpu}：比 60 强一截，钱包也瘦一截。", "{gpu}：比 60 強一截，錢包也瘦一截。", "{gpu}: a step above the 60, and a step lighter wallet."));
                }
                else if (tier >= 80)
                {
                    l.Add(J("飞了飞了！{gpu} 都上了，这台电脑比我还贵吧？", "起飛啦！連 {gpu} 都上了，這台電腦比我還貴吧？", "We're flying! A {gpu}? This laptop costs more than me, doesn't it?"));
                    l.Add(J("{gpu}：性能怪兽，插电起飞，拔电回到解放前。", "{gpu}：效能怪獸，插電起飛，拔電打回原形。", "{gpu}: absolute unit when plugged in. Unplugged? Back to the stone age."));
                    l.Add(J("这么强的显卡，用来开写字板是不是有点浪费？", "這麼強的顯卡拿來開 WordPad，是不是有點浪費？", "A GPU this strong... and you're using it for WordPad?"));
                    l.Add(J("{gpu} 满载的时候，风扇声音像要起飞。", "{gpu} 滿載的時候，風扇聲像飛機要起飛。", "{gpu} at full load: the fans sound like a jet taking off."));
                    if (tier == 90) l.Add(J("{gpu}：笔记本里的天花板，看到它我都要立正敬礼。", "{gpu}：筆電界的天花板，看到它我都要立正敬禮。", "{gpu}: the laptop ceiling. I salute."));
                    if (model >= 5000) l.Add(J("50 系旗舰都安排上了，贴吧老哥看了直呼羡慕。", "50 系列旗艦都安排上了，鄉民看了直呼羨慕。", "50-series flagship? r/pcmasterrace is jealous."));
                    if (L.En) l.Add("{gpu}: but can it run Crysis? Yes. Finally, yes.");
                }
                if (model >= 2000 && model < 3000) l.Add(J("20 系：光追元年，开光追就是幻灯片元年。", "20 系列：光追元年，開光追就是投影片元年。", "RTX 20: year one of ray tracing, and of the slideshow."));
                if (model >= 3000 && model < 4000) l.Add(J("30 系：挖矿那几年的回忆，你懂的。", "30 系列：挖礦那幾年的回憶，你懂的。", "RTX 30: flashbacks to the crypto-mining shortage. You know."));
                if (model >= 4000 && model < 5000) l.Add(J("40 系：DLSS 3 帧生成，帧数是“算”出来的。", "40 系列：DLSS 3 幀生成，幀數是「算」出來的。", "RTX 40: DLSS 3 frame generation. Fake frames, real smiles."));
                if (model >= 5000) l.Add(J("50 系：多帧生成，帧数比你的作业还能编。", "50 系列：多幀生成，幀數比你的報告還會掰。", "RTX 50: multi-frame gen. 5070 = 4090 performance (trust me bro)."));
            }
            else if (Is(g, @"Radeon\s+RX"))
            {
                l.Add(J("AMD YES！{gpu} 在此。", "AMD YES！{gpu} 在此。", "AMD YES! {gpu} reporting in."));
                l.Add(J("A 卡驱动：每次更新都是开盲盒。", "A 卡驅動：每次更新都像開盲盒。", "AMD drivers: every update is a loot box."));
                l.Add(J("{gpu}：同价位性能不错，就是游戏优化看缘分。", "{gpu}：同價位效能不錯，遊戲最佳化就看緣分。", "{gpu}: great for the price. Game optimization depends on the stars."));
            }
            else if (Is(g, @"\bArc\b"))
            {
                l.Add(J("{gpu}：牙膏厂也造显卡了，驱动一天比一天好。", "{gpu}：牙膏廠也做顯卡了，驅動一天比一天好。", "{gpu}: Intel makes GPUs now. The drivers get better every day."));
                l.Add(J("Intel Arc：游戏随缘，视频编码倒是一把好手。", "Intel Arc：遊戲隨緣，影片編碼倒是一把好手。", "Intel Arc: gaming is hit or miss, but the video encoder slaps."));
            }
            return l;
        }

        // ---------------- CPU ----------------
        static IEnumerable<string> Cpu(string c)
        {
            var l = new List<string>();
            if (Is(c, @"Celeron|Pentium|\bN\d{3}\b|Athlon"))
            {
                l.Add(J("{cpu}：能开机就是胜利。", "{cpu}：能開機就是勝利。", "{cpu}: booting up is a win."));
                l.Add(J("开个网页要转圈？别急，{cpu} 正在全力思考。", "開個網頁也要轉圈？別急，{cpu} 正在全力思考。", "Loading spinner on a web page? Relax, {cpu} is thinking really hard."));
            }
            else if (Is(c, @"Core\s+Ultra"))
            {
                l.Add(J("Core Ultra：名字里带 Ultra，主打一个 AI 加持。", "Core Ultra：名字有 Ultra，主打 AI 加持。", "Core Ultra: 'Ultra' in the name, AI on every slide."));
                l.Add(J("{cpu}：大小核加 NPU，就是不知道 NPU 平时在干嘛。", "{cpu}：大小核加 NPU，就是不知道 NPU 平常在幹嘛。", "{cpu}: P-cores, E-cores and an NPU. Nobody knows what the NPU does all day."));
            }
            else if (Is(c, @"\bi3-"))
            {
                l.Add(J("{cpu}：没有 i9 的命，但有 i3 的稳。", "{cpu}：沒有 i9 的命，但有 i3 的穩。", "{cpu}: not i9 money, but i3 reliable."));
                l.Add(J("i3：日常办公够用，开 30 个网页标签另说。", "i3：日常文書夠用，開 30 個分頁另當別論。", "i3: fine for office work. 30 Chrome tabs is another story."));
                l.Add(J("i3 打游戏？可以，只要游戏是扫雷。", "i3 打遊戲？可以，只要遊戲是踩地雷。", "Gaming on an i3? Sure, if the game is Minesweeper."));
            }
            else if (Is(c, @"\bi5-"))
            {
                l.Add(J("i5：甜品级处理器，啥都能干一点。", "i5：甜品級處理器，什麼都能做一點。", "i5: the sweet spot. A little good at everything."));
                l.Add(J("{cpu}：不上不下的 i5，才是真正的中流砥柱。", "{cpu}：不上不下的 i5，才是真正的中流砥柱。", "{cpu}: the mid-tier i5 quietly holding up the whole world."));
            }
            else if (Is(c, @"\bi7-"))
            {
                l.Add(J("i7：主流高配，风扇声就是它在努力的证明。", "i7：主流高配，風扇聲就是它努力的證明。", "i7: high-end mainstream. The fan noise is proof it's trying."));
                l.Add(J("{cpu}：性能够用，就是夏天有点暖手。", "{cpu}：效能夠用，就是夏天有點暖手。", "{cpu}: plenty fast. A bit toasty in summer."));
            }
            else if (Is(c, @"\bi9-"))
            {
                l.Add(J("i9：性能怪兽，也是冬天的暖手宝。", "i9：效能怪獸，也是冬天的暖暖包。", "i9: performance monster and winter hand warmer."));
                l.Add(J("{cpu}：夏天别放腿上，会烫。", "{cpu}：夏天別放腿上，會燙。", "{cpu}: don't put it on your lap in summer. Trust me."));
            }
            // 13th / 14th-gen i7 / i9 (Raptor Lake): the "雷电法王" of DIY forums — high voltage, high power.
            // The degradation ("缩缸") issue was confirmed for desktop chips; Intel says mobile ones aren't affected.
            if (Is(c, @"\bi[79]-1[34]\d{3}"))
            {
                l.Add(J("⚡ 雷电法王登场！{cpu}：电压给得足，性能拉得满。", "⚡ 雷電法王登場！{cpu}：電壓給好給滿，效能拉滿。", "⚡ UNLIMITED POWER! {cpu}: full voltage, full send."));
                l.Add(J("电压大法好！散热器听了都想辞职。", "電壓大法好！散熱器聽了都想離職。", "Voltage go brrr. The cooler wants to quit."));
                if (Is(c, @"\bi[79]-1[34]\d{3}(H|HX|P|U)\b"))
                    l.Add(J("雷电法王的笔记本版：Intel 说移动版不缩缸，那就……信一次？", "雷電法王筆電版：Intel 說行動版不會縮缸，那就……信一次？", "Raptor Lake, laptop edition: Intel says mobile chips don't degrade. Trust me bro?"));
                else
                {
                    l.Add(J("{cpu}：记得更新 BIOS 微码，缩缸这事 DIY 老哥都懂。", "{cpu}：記得更新 BIOS 微碼，縮缸這件事玩家都懂。", "{cpu}: update your BIOS microcode. The degradation saga, you know the one."));
                    l.Add(J("13/14 代 i9：雷电法王，主打一个高压放电。", "13/14 代 i9：雷電法王，主打高壓放電。", "13th/14th-gen i9: the Emperor Palpatine of CPUs."));
                }
            }
            if (Is(c, @"Intel"))
                l.Add(J("牙膏厂的 CPU：牙膏要一点一点挤才香。", "牙膏廠的 CPU：牙膏要一點一點擠才香。", "Intel: +5% per generation. Gotta squeeze that toothpaste slowly."));

            var ryzen = Regex.Match(c, @"Ryzen\s+(AI\s+)?(\d)", RegexOptions.IgnoreCase);
            if (ryzen.Success)
            {
                l.Add(J("AMD YES！{cpu} 在线。", "AMD YES！{cpu} 上線。", "AMD YES! {cpu} online."));
                int n = int.Parse(ryzen.Groups[2].Value);
                if (n == 3) l.Add(J("R3：AMD 家的小弟，省电够用。", "R3：AMD 家的小老弟，省電夠用。", "R3: AMD's little bro. Efficient and enough."));
                if (n == 5) l.Add(J("R5：性价比之王，贴吧推荐常客。", "R5：CP 值之王，鄉民推薦常客。", "R5: value king, every build guide's default pick."));
                if (n == 7) l.Add(J("R7：多核战神，渲染导出不慌。", "R7：多核戰神，算圖輸出不慌。", "R7: multi-core beast. Renders don't scare it."));
                if (n == 9) l.Add(J("R9：核多就是任性，编译代码飞快。", "R9：核心多就是任性，編譯超快。", "R9: so many cores it's showing off. Compiles fly."));
                if (!string.IsNullOrEmpty(ryzen.Groups[1].Value)) l.Add(J("Ryzen AI：名字里也带 AI 了，AI 浓度超标。", "Ryzen AI：名字也帶 AI 了，AI 濃度超標。", "Ryzen AI: AI in the name too. AI levels critical."));
            }
            return l;
        }
    }
}
