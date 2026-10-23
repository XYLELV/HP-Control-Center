# HP 控制台（HP Control Center）

Windows 笔记本的二次元风格控制台：在一个窗口里看硬件状态、调常用设置，右下角有看板娘「HP 娘」陪着。

## 能做什么

- **硬件监控**：CPU / GPU 温度、CPU / GPU 占用、内存、显存、主板温度、风扇转速；最近 60 秒温度曲线（CPU 蓝色、GPU 橙色）
- **详细配置**：处理器、主板、BIOS、内存、显卡、显示器、磁盘、声卡、网卡、电池、系统，一键复制
- **常用控制**：屏幕亮度、音量与静音、电源模式（省电 / 平衡 / 高性能）
- **快捷开关**：WLAN、蓝牙、触控板、摄像头、麦克风
- **电池**：健康度、满充 / 设计容量、循环次数、充放电功率、预计剩余时间或充满时间
- **快捷启动**：打开或关闭常用程序，可添加自己的程序
- **HP 娘**：根据温度、电量、时间说台词，过热和低电量时提醒；显示当地天气（Windows 定位 / 按 IP / 手动选城市）
- **系统操作**：锁屏、睡眠、重启、关机
- **设置**：语言、人物图片、主题色、刷新间隔、温度提醒、开机自启、窗口置顶
- **三种语言**：简体中文 / 繁體中文 / English，HP 娘的台词和梗按语言分别编写（不是机翻）

所有配置信息都从运行它的电脑实时读取，没有写死某一台机器。

## 风扇与 EC

风扇转速和 EC 温度通过 [PawnIO](https://github.com/namazso/PawnIO) 从主板嵌入式控制器（EC）**只读**读取：程序只发送 EC 读取命令 `0x80`，不写入任何风扇或硬件设置。

已适配的主板：HP 8819（HP 240 G8）。其他机型显示「此机型未适配」，CPU 温度改用 Windows 的 ACPI 温度区。要适配新机型，在 `src/Machine.cs` 的 `EcProfile.For` 里加一项寄存器表。

## 运行要求

- Windows 10 / 11（64 位），.NET Framework 4.8（系统自带）
- 管理员权限（读取 EC、开关触控板和摄像头）
- 读取风扇需要先安装 [PawnIO](https://github.com/namazso/PawnIO.Setup/releases)

## 编译与安装

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File install.ps1
```

- `build.ps1` 使用 Windows 自带的 C# 编译器，不需要 Visual Studio；会自动下载 PawnIO 的 `LpcACPIEC.bin` 模块并校验 SHA-256。
- `install.ps1` 安装到 `C:\Program Files\HPControlPanel`（只有管理员能修改），并创建开始菜单项「HP Control Center」。设置保存在 `%APPDATA%\HPControlPanel`。

## 联网说明

- 天气：[Open-Meteo](https://open-meteo.com/)（天气）、[OpenStreetMap Nominatim](https://nominatim.openstreetmap.org/)（地名和城市搜索）、[ip.sb](https://ip.sb/)（「按 IP」模式）。「Windows 定位」模式只发送精度约 10 公里的坐标。
- 更新检查：每天最多一次访问 GitHub 的发布页（可在设置里关闭）。有新版本时只在「设置 → 关于」里提示，不弹窗。

## 说明

- 作者：HoShino_Rizu
- 非 HP 官方软件；HP 是惠普公司的商标。
- 图片（HP 娘、图标、界面效果图）由 GPT image 模型制作。
- 代码由 Claude Opus 5.5 全程编写。
