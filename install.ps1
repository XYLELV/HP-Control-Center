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
#  - If the PawnIO driver (needed to read the fan) isn't installed, its official signed setup is downloaded from
#    github.com/namazso/PawnIO.Setup, checked against the SHA-256 below and installed silently.
#    -NoPawnIO skips this.
param([switch]$NoPawnIO)
$ErrorActionPreference = 'Stop'
# Source checkout: files are in bin\ after build.ps1. Release zip: they sit next to this script.
$src = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path (Join-Path $src 'HPControlPanel.exe'))) { $src = $PSScriptRoot }
$dst = Join-Path $env:ProgramFiles 'HPControlPanel'
$files = 'HPControlPanel.exe', 'LpcACPIEC.bin'
$pawnUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
$pawnSha = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'

$stage = Join-Path $env:TEMP ('hpcp-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$hashes = @{}
foreach ($f in $files) {
    Copy-Item (Join-Path $src $f) $stage
    $hashes[$f] = (Get-FileHash (Join-Path $stage $f) -Algorithm SHA256).Hash
}

$needPawn = -not $NoPawnIO -and -not (Test-Path (Join-Path $env:ProgramFiles 'PawnIO\PawnIOLib.dll'))
if ($needPawn) {
    'downloading PawnIO 2.2.0 ...'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        (New-Object Net.WebClient).DownloadFile($pawnUrl, (Join-Path $stage 'PawnIO_setup.exe'))
        if ((Get-FileHash (Join-Path $stage 'PawnIO_setup.exe') -Algorithm SHA256).Hash -ne $pawnSha) { throw 'PawnIO_setup.exe SHA-256 mismatch' }
    } catch {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        throw ('PawnIO download failed: ' + $_.Exception.Message + ' (run again when online, or use -NoPawnIO)')
    }
}
# Run by the elevated step only after it has copied the setup into the admin-only folder and re-checked it there.
$pawnStep = if ($needPawn) { @"
    `$ps = Join-Path `$dst 'PawnIO_setup.exe'
    Copy-Item (Join-Path `$stage 'PawnIO_setup.exe') `$ps -Force
    if ((Get-FileHash `$ps -Algorithm SHA256).Hash -ne '$pawnSha') { Remove-Item `$ps -Force; throw 'PawnIO setup changed after download' }
    `$pp = Start-Process `$ps -ArgumentList '-install', '-silent' -Wait -PassThru
    Remove-Item `$ps -Force
    if (`$pp.ExitCode -eq 3010) { 'pawnio ok (restart needed)' | Add-Content `$log } elseif (`$pp.ExitCode -eq 0) { 'pawnio ok' | Add-Content `$log } else { ('pawnio exit ' + `$pp.ExitCode) | Add-Content `$log }
"@ } else { '' }

# Verified AFTER copying, on the copy in the admin-only destination (checking the staged file first would leave
# a window to swap it between the check and the copy).
$checks = ($files | ForEach-Object { "if ((Get-FileHash (Join-Path `$dst '$_') -Algorithm SHA256).Hash -ne '$($hashes[$_])') { Remove-Item (Join-Path `$dst '$_') -Force; throw 'file changed after staging: $_' }" }) -join "`n"
# Paths go into the elevated script as single-quoted literals, so any ' in them (e.g. a user named O'Brien) is doubled.
$stageLit = $stage.Replace("'", "''")
$dstLit = $dst.Replace("'", "''")
$elevated = @"
`$ErrorActionPreference = 'Stop'
`$stage = '$stageLit'
`$dst = '$dstLit'
`$log = Join-Path `$stage 'result.txt'
try {
    New-Item -ItemType Directory -Force -Path `$dst | Out-Null
    Get-ChildItem `$dst -Filter 'HPControlPanel.*.old' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-Item `$_.FullName -Force } catch {} }
    `$exe = Join-Path `$dst 'HPControlPanel.exe'
    if (Test-Path `$exe) { try { Remove-Item `$exe -Force } catch { Rename-Item `$exe ('HPControlPanel.{0:yyyyMMddHHmmss}.old' -f (Get-Date)) } }
    foreach (`$f in '$($files -join "','")') { Copy-Item (Join-Path `$stage `$f) `$dst -Force }
$checks
$pawnStep
    # All-users Start menu entry (ProgramData, admin-only), so Windows search finds "HP Control Center".
    `$common = Join-Path `$env:ProgramData 'Microsoft\Windows\Start Menu\Programs\HP Control Center.lnk'
    `$ws = New-Object -ComObject WScript.Shell
    `$l = `$ws.CreateShortcut(`$common); `$l.TargetPath = `$exe; `$l.WorkingDirectory = `$dst; `$l.IconLocation = `$exe + ',0'; `$l.Description = 'HP Control Center'; `$l.Save()
    Add-Type 'using System; using System.Runtime.InteropServices; public static class HpcpNotify { [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern void SHChangeNotify(int e, uint f, string a, IntPtr b); }'
    [HpcpNotify]::SHChangeNotify(2, 5, `$common, [IntPtr]::Zero)
    [HpcpNotify]::SHChangeNotify(0x1000, 5, (Split-Path `$common), [IntPtr]::Zero)
    'start menu ok' | Add-Content `$log
    # Autostart tasks: one per account ("HPControlPanel-<SID>"), plus the older shared "HPControlPanel".
    foreach (`$t in @(Get-ScheduledTask -TaskName 'HPControlPanel*' -ErrorAction SilentlyContinue)) {
        try { Set-ScheduledTask -TaskName `$t.TaskName -TaskPath `$t.TaskPath -Action (New-ScheduledTaskAction -Execute `$exe) | Out-Null; 'task repointed' | Add-Content `$log }
        catch { ('task not repointed: ' + `$t.TaskName) | Add-Content `$log }
    }
    'ok' | Add-Content `$log
} catch { ('error: ' + `$_.Exception.Message) | Add-Content `$log }
"@
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevated))
try { $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $enc }
catch { }   # UAC declined: reported below as "no result"
$result = if (Test-Path "$stage\result.txt") { Get-Content "$stage\result.txt" } else { 'no result (UAC declined?)' }
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$result
if ($result -notcontains 'ok') { throw "install failed: $result" }
if ($result -match '^pawnio exit') { Write-Warning 'PawnIO did not install, so fan speed and EC temperatures will not be readable. Everything else works. Install it from https://github.com/namazso/PawnIO.Setup/releases' }
if ($result -contains 'pawnio ok (restart needed)') { Write-Warning 'PawnIO was installed. Restart Windows once before fan readings work.' }

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
