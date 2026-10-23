# Installs the built app to C:\Program Files\HPControlPanel (writable by administrators only), so nothing the
# elevated app or its autostart task runs can be replaced by a normal user process.
#  - Files are staged in %TEMP% and copied by one elevated PowerShell whose command is passed inline
#    (-EncodedCommand, no script file to tamper with); it checks the SHA-256 of the copies in Program Files
#    and removes any that changed after staging.
#  - If the app is running, its exe is renamed aside (Windows allows renaming a running exe).
#  - An existing "HPControlPanel" autostart task is re-pointed to the new exe.
#  - The old per-user install (%LOCALAPPDATA%\Programs\HPControlPanel) loses its exe; settings.ini stays there
#    until the app migrates it to %APPDATA%\HPControlPanel on its next start.
#  - The desktop shortcut is re-pointed and an all-users Start menu entry "HP Control Center" is written;
#    the app removes the old per-user Start menu entry when it starts.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'bin'
$dst = Join-Path $env:ProgramFiles 'HPControlPanel'
$files = 'HPControlPanel.exe', 'LpcACPIEC.bin'

$stage = Join-Path $env:TEMP ('hpcp-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$hashes = @{}
foreach ($f in $files) {
    Copy-Item (Join-Path $src $f) $stage
    $hashes[$f] = (Get-FileHash (Join-Path $stage $f) -Algorithm SHA256).Hash
}

# Verified AFTER copying, on the copy in the admin-only destination (checking the staged file first would leave
# a window to swap it between the check and the copy).
$checks = ($files | ForEach-Object { "if ((Get-FileHash (Join-Path `$dst '$_') -Algorithm SHA256).Hash -ne '$($hashes[$_])') { Remove-Item (Join-Path `$dst '$_') -Force; throw 'file changed after staging: $_' }" }) -join "`n"
$elevated = @"
`$ErrorActionPreference = 'Stop'
`$stage = '$stage'
`$dst = '$dst'
`$log = Join-Path `$stage 'result.txt'
try {
    New-Item -ItemType Directory -Force -Path `$dst | Out-Null
    Get-ChildItem `$dst -Filter 'HPControlPanel.*.old' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-Item `$_.FullName -Force } catch {} }
    `$exe = Join-Path `$dst 'HPControlPanel.exe'
    if (Test-Path `$exe) { try { Remove-Item `$exe -Force } catch { Rename-Item `$exe ('HPControlPanel.{0:yyyyMMddHHmmss}.old' -f (Get-Date)) } }
    foreach (`$f in '$($files -join "','")') { Copy-Item (Join-Path `$stage `$f) `$dst -Force }
$checks
    # All-users Start menu entry (ProgramData, admin-only), so Windows search finds "HP Control Center".
    `$common = Join-Path `$env:ProgramData 'Microsoft\Windows\Start Menu\Programs\HP Control Center.lnk'
    `$ws = New-Object -ComObject WScript.Shell
    `$l = `$ws.CreateShortcut(`$common); `$l.TargetPath = `$exe; `$l.WorkingDirectory = `$dst; `$l.IconLocation = `$exe + ',0'; `$l.Description = 'HP Control Center'; `$l.Save()
    Add-Type 'using System; using System.Runtime.InteropServices; public static class HpcpNotify { [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern void SHChangeNotify(int e, uint f, string a, IntPtr b); }'
    [HpcpNotify]::SHChangeNotify(2, 5, `$common, [IntPtr]::Zero)
    [HpcpNotify]::SHChangeNotify(0x1000, 5, (Split-Path `$common), [IntPtr]::Zero)
    'start menu ok' | Add-Content `$log
    `$task = Get-ScheduledTask -TaskName 'HPControlPanel' -ErrorAction SilentlyContinue
    if (`$task) { Set-ScheduledTask -TaskName 'HPControlPanel' -Action (New-ScheduledTaskAction -Execute `$exe) | Out-Null; 'task repointed' | Add-Content `$log }
    'ok' | Add-Content `$log
} catch { ('error: ' + `$_.Exception.Message) | Add-Content `$log }
"@
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevated))
$p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $enc
$result = if (Test-Path "$stage\result.txt") { Get-Content "$stage\result.txt" } else { 'no result (UAC declined?)' }
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$result
if ($result -notcontains 'ok') { throw "install failed: $result" }

# Old per-user copy: remove the exe and module so nothing can run from there any more.
$old = Join-Path $env:LOCALAPPDATA 'Programs\HPControlPanel'
foreach ($f in Get-ChildItem $old -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'HPControlPanel*.exe' -or $_.Name -like 'HPControlPanel.*.old' -or $_.Name -eq 'LpcACPIEC.bin' }) {
    try { Remove-Item $f.FullName -Force } catch { Rename-Item $f.FullName ($f.Name + '.removed') -ErrorAction SilentlyContinue }
}

# Desktop shortcut "HP <kongzhitai>.lnk" (name built from code points: this script stays ASCII for PowerShell 5.1).
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) ('HP ' + [char]0x63A7 + [char]0x5236 + [char]0x53F0 + '.lnk')
if (Test-Path $lnk) {
    $sh = New-Object -ComObject WScript.Shell
    $s = $sh.CreateShortcut($lnk)
    $s.TargetPath = Join-Path $dst 'HPControlPanel.exe'; $s.WorkingDirectory = $dst; $s.IconLocation = (Join-Path $dst 'HPControlPanel.exe') + ',0'
    $s.Save()
}
"installed: $dst"
