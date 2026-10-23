HP 控制台 / HP Control Center
https://github.com/XYLELV/HP-Control-Center

【怎么用】
1. 双击 Install.cmd：安装到 C:\Program Files\HPControlPanel，加开始菜单项「HP Control Center」，
   电脑没有 PawnIO 驱动时自动下载官方版安装（读取风扇需要它）。会弹一次管理员确认。
2. 或者直接双击 HPControlPanel.exe 免安装使用（LpcACPIEC.bin 要和它放在同一个文件夹）。
   免安装时不能开机自启（开机自启只允许安装版使用）。

exe 没有代码签名，第一次打开可能出现「Windows 已保护你的电脑」：点「更多信息」→「仍要运行」。
不放心的话，源码全部公开，可以自己编译（见 GitHub 上的 README）。

【How to use】
1. Double-click Install.cmd: installs to C:\Program Files\HPControlPanel, adds a Start menu entry and,
   if missing, installs the official PawnIO driver (needed for fan readings). Asks for administrator once.
2. Or just run HPControlPanel.exe from this folder (keep LpcACPIEC.bin next to it).
   "Start with Windows" needs the installed copy.

The exe is not code-signed, so SmartScreen may warn on first run: "More info" → "Run anyway".
All source code is public; you can build it yourself instead.

【包含的第三方文件 / Third-party files】
LpcACPIEC.bin — PawnIO EC module 0.2.11, LGPL-2.1, source: https://github.com/namazso/PawnIO.Modules
PawnIO driver (downloaded by install.ps1 when needed) — https://github.com/namazso/PawnIO.Setup

非 HP 官方软件；HP 是惠普公司的商标。Not affiliated with HP Inc.
完全开源，欢迎有能力的代码高手一起改进。这个项目只是闲着没事用 Claude 做着玩的。
