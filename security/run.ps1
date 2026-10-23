# Builds and runs the red/blue security test against the built HP Control Center exe.
# It attacks only this app's own defenses, in a temp playground, as an ordinary user. Run build.ps1 first.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$exe = Join-Path $root 'bin\HPControlPanel.exe'
if (-not (Test-Path $exe)) { throw "bin\HPControlPanel.exe missing - run build.ps1 first" }

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
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
exit $code
