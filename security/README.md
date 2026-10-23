# 安全测试：红队 vs 蓝队

验证 v1.0.1 修掉的提权问题是否真的挡得住。只针对本项目自己的 app、在临时文件夹里、以普通用户身份运行，不碰别的程序或别的机器。

- **蓝队（防守）：** `src/SafeFile.cs`（读和写都防 junction / 软链接 / 硬链接）、`Settings.IsInstallPath`（只允许 `C:\Program Files\HPControlPanel\HPControlPanel.exe` 开机自启）、`SystemInfo.Sys32`（系统工具走 System32 完整路径）。
- **红队（进攻）：** `RedBlue.cs`，依次尝试审查里发现的 6 个攻击点，每项输出 `BLOCKED` / `ALLOWED` / `INCONCLUSIVE`。

## 运行

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File security\run.ps1
```

先跑一轮对照：确认测试进程**没有**以管理员运行，且 `SafeFile` 对普通文件的写入、覆盖、追加、读取都正常（否则一个"什么都拒绝"的实现也能全部 `BLOCKED`）。判定看实际结果（哨兵内容有没有变、目标位置有没有多出文件），不看函数返回值。`run.ps1` 会打印被测 exe 的路径和 SHA-256。

退出码：0 = 全部挡住；1 = 有 `ALLOWED`；2 = 有 `INCONCLUSIVE`（攻击没搭起来，结论不可信）；3 = 对照失败。

## 6 个攻击点

| # | 攻击 | 防守 |
|---|------|------|
| 1 | 把 `%APPDATA%\HPControlPanel` 换成指向别处的 junction，让提权进程写到别处 | `SafeFile.DirOk`：目录解析不回自身就拒写 |
| 2 | 把 settings.ini / 人物图片预先做成指向受保护文件的硬链接 | `SafeFile`：打开后发现链接数 > 1 就拒写 |
| 3 | 在运行目录放假 `pnputil.exe` / `shutdown.exe` | 一律用 System32 完整路径启动；启动时还把当前目录设为 System32、从 DLL 搜索路径移除 |
| 4 | 非安装版副本（用户目录、Program Files 下别的文件夹、`..` 绕出、子目录）注册以管理员身份运行的开机自启 | `Settings.IsInstallPath`：只认精确的安装路径且该目录无 junction，`SetAutoStart(true)` 拒绝 |
| 5 | 把提权进程要**读**的目录（旧版迁移目录）换成 junction，把用户读不到的文件复制出来 | `SafeFile.ReadAllBytes`：同样的目录 / 最终路径 / 链接数检查，从已校验的句柄读，并限制大小 |
| 6 | 把要读的文件（如 custom-mascot.png）做成指向别的文件的硬链接 | `SafeFile.ReadAllBytes`：链接数 > 1 就拒读 |

## 给红队

欢迎扩充 `RedBlue.cs`，针对本项目继续找洞：EC 只读边界、电池 IOCTL、PDH 解析、WinRT 反射调用、`install.ps1` 的提权段、更新检查的网络请求等。找到能让某一项变成 `ALLOWED` 的做法，就是真问题——提 Issue 或 PR，会修。
