# 安全测试：红队 vs 蓝队

验证 v1.0.1 修掉的提权问题是否真的挡得住。只针对本项目自己的 app、在临时文件夹里、以普通用户身份运行，不碰别的程序或别的机器。

- **蓝队（防守）：** `src/SafeFile.cs`（防 junction / 软链接 / 硬链接）、`Settings.InstalledCopy`（只允许安装版开机自启）、`SystemInfo.Sys32`（系统工具走 System32 完整路径）。
- **红队（进攻）：** `RedBlue.cs`，依次尝试审查里发现的 4 个攻击点，每项输出 `BLOCKED` / `ALLOWED`。

## 运行

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File security\run.ps1
```

全部 `BLOCKED` 时退出码为 0；任一 `ALLOWED` 退出码为 1。

## 4 个攻击点

| # | 攻击 | 防守 |
|---|------|------|
| 1 | 把 `%APPDATA%\HPControlPanel` 换成指向别处的 junction，让提权进程写到别处 | `SafeFile.DirOk`：目录解析不回自身就拒写 |
| 2 | 把 settings.ini / 人物图片预先做成指向受保护文件的硬链接 | `SafeFile`：打开后发现链接数 > 1 就拒写 |
| 3 | 在运行目录放假 `pnputil.exe` / `shutdown.exe` | 一律用 System32 完整路径启动；启动时还把当前目录设为 System32、从 DLL 搜索路径移除 |
| 4 | 免安装副本（用户可写目录）注册以管理员身份运行的开机自启 | `Settings.InstalledCopy` 在 Program Files 之外为 false，`SetAutoStart(true)` 拒绝 |

## 给红队

欢迎扩充 `RedBlue.cs`，针对本项目继续找洞：EC 只读边界、电池 IOCTL、PDH 解析、WinRT 反射调用、`install.ps1` 的提权段、更新检查的网络请求等。找到能让某一项变成 `ALLOWED` 的做法，就是真问题——提 Issue 或 PR，会修。
