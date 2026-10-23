# Builds the release zip dist\HP-Control-Center-<version>.zip from bin\ (run build.ps1 first):
# HPControlPanel.exe, LpcACPIEC.bin, install.ps1, Install.cmd and README.txt in one folder.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = [regex]::Match((Get-Content "$root\src\Updater.cs" -Raw), 'Version\s*=\s*"([^"]+)"').Groups[1].Value
$name = "HP-Control-Center-$version"
$dist = Join-Path $root 'dist'
$dir = Join-Path $dist $name
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dir | Out-Null

foreach ($f in 'HPControlPanel.exe', 'LpcACPIEC.bin') {
    if (-not (Test-Path "$root\bin\$f")) { throw "bin\$f missing - run build.ps1 first" }
    Copy-Item "$root\bin\$f" $dir
}
Copy-Item "$root\install.ps1" $dir
# Windows line endings for the batch file; UTF-8 with BOM so every Notepad version shows the Chinese text.
$crlf = { param($p) ((Get-Content $p -Raw -Encoding UTF8) -replace "`r?`n", "`r`n") }
[IO.File]::WriteAllText("$dir\Install.cmd", (& $crlf "$root\package\Install.cmd"), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText("$dir\README.txt", (& $crlf "$root\package\README.txt"), (New-Object Text.UTF8Encoding($true)))

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entries are added one by one with "/" separators (ZipFile.CreateFromDirectory on .NET Framework writes "\",
# which some unzip tools turn into file names containing backslashes).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem $dir -File) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, "$name/$($f.Name)", [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Remove-Item $dir -Recurse -Force
"packaged: $zip"
(Get-FileHash $zip -Algorithm SHA256).Hash
