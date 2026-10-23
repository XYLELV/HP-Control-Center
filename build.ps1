# Builds bin\HPControlPanel.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# -Snapshot builds bin-snap\HPControlPanel.exe without the administrator manifest, for layout screenshots only.
param([switch]$Snapshot)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

$refs = @(
    "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
    "$fx\System.Xaml.dll", "$fx\System.Management.dll", "$fx\Microsoft.CSharp.dll", "$fx\System.Core.dll",
    "$fx\System.Device.dll", "$fx\System.Web.Extensions.dll"
) | ForEach-Object { "/r:$_" }

$sources = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }

$outDir = if ($Snapshot) { "$root\bin-snap" } else { "$root\bin" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$manifest = if ($Snapshot) { "/nowin32manifest" } else { "/win32manifest:$root\app.manifest" }

& $csc /nologo /target:winexe /platform:x64 /optimize /utf8output `
    $manifest "/win32icon:$root\assets\app-anime.ico" `
    "/resource:$root\src\MainWindow.xaml,MainWindow.xaml" `
    "/resource:$root\assets\hp-girl-v2-home.png,girl-home.png" `
    "/resource:$root\assets\hp-girl-v2-avatar.png,girl-avatar.png" `
    "/resource:$root\assets\app-anime.ico,app.ico" `
    "/resource:$root\assets\app-icon-128.png,app-icon.png" `
    "/out:$outDir\HPControlPanel.exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
"built: $outDir\HPControlPanel.exe"

# PawnIO's signed EC module (LGPL-2.1, github.com/namazso/PawnIO.Modules) is fetched, not committed.
$module = Join-Path $outDir 'LpcACPIEC.bin'
if (-not $Snapshot -and -not (Test-Path $module)) {
    $zip = Join-Path $env:TEMP ('pawnio-modules-' + [guid]::NewGuid().ToString('N') + '.zip')
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing 'https://github.com/namazso/PawnIO.Modules/releases/download/0.2.11/release_0_2_11.zip' -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne '43608CB89BC84247FEF1368A139013F7D043E17DB6D6C8DFC9B46BF0905A81F4') { Remove-Item $zip; throw 'PawnIO modules zip: unexpected SHA-256' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try { [IO.Compression.ZipFileExtensions]::ExtractToFile(($z.Entries | Where-Object Name -eq 'LpcACPIEC.bin' | Select-Object -First 1), $module, $true) }
    finally { $z.Dispose(); Remove-Item $zip }
    "fetched: $module"
}
