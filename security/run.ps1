# Builds and runs the red/blue security test against the built HP Control Center exe.
# It attacks only this app's own defenses, in a temp playground, as an ordinary user. Run build.ps1 first.
$ErrorActionPreference = 'Stop'
# The exe under test: the repo's bin\ build, or (in a handoff package, which has no bin\) the one next to this script.
# Its path and SHA-256 are printed, so a result can always be tied to the exact build it tested.
$exe = Join-Path (Split-Path $PSScriptRoot) 'bin\HPControlPanel.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $PSScriptRoot 'HPControlPanel.exe' }
if (-not (Test-Path $exe)) { throw "HPControlPanel.exe missing - run build.ps1 first" }
"testing: $exe"
"SHA-256: $((Get-FileHash $exe -Algorithm SHA256).Hash)"

# The test runs from its own temp folder, so Settings.InstalledCopy sees a portable (non-Program Files) copy.
$out = Join-Path ([IO.Path]::GetTempPath()) ('hpcp-redblue-bin-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $exe (Join-Path $out 'HPControlPanel.exe')

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& $csc /nologo /target:exe /platform:x64 "/out:$out\RedBlue.exe" `
    "/r:$out\HPControlPanel.exe" "/r:$wpf\PresentationFramework.dll" "/r:$wpf\WindowsBase.dll" `
    "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Management.dll" `
    "$PSScriptRoot\RedBlue.cs"
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

& "$out\RedBlue.exe"
$code = $LASTEXITCODE
"exit code $code (0 = all held, 1 = got through, 2 = inconclusive, 3 = control failed)"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
exit $code
