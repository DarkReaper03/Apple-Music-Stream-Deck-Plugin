# Builds the Apple Music Stream Deck plugin and (unless -NoInstall) installs it.
# Installing restarts the Stream Deck app, since it only loads plugins at startup.
#
# -Package builds a self-contained copy (no .NET install needed to run it) and writes
# dist\com.adamc.applemusic.streamDeckPlugin, which installs with a double-click.
param([switch]$NoInstall, [switch]$Package)

$ErrorActionPreference = 'Stop'
$uuid = 'com.adamc.applemusic'
$pluginDir = Join-Path $PSScriptRoot "$uuid.sdPlugin"
$binDir = Join-Path $pluginDir 'bin'
$project = Join-Path $PSScriptRoot 'src\AppleMusicPlugin\AppleMusicPlugin.csproj'

& (Join-Path $PSScriptRoot 'tools\make-icons.ps1') -OutDir (Join-Path $pluginDir 'imgs')

if ($Package) {
    $distDir = Join-Path $PSScriptRoot 'dist'
    $stageDir = Join-Path $distDir 'stage'
    $stagedPlugin = Join-Path $stageDir "$uuid.sdPlugin"
    $packageFile = Join-Path $distDir "$uuid.streamDeckPlugin"

    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Force $stagedPlugin | Out-Null
    Copy-Item (Join-Path $pluginDir '*') $stagedPlugin -Recurse -Exclude 'bin'

    dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -o (Join-Path $stagedPlugin 'bin') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    # A .streamDeckPlugin file is a zip with the .sdPlugin folder at its root.
    # Windows' tar writes zip entries with forward slashes, which Stream Deck expects.
    if (Test-Path $packageFile) { Remove-Item $packageFile -Force }
    $zipFile = "$packageFile.zip"
    & tar.exe -a -c -f $zipFile -C $stageDir "$uuid.sdPlugin"
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
    Move-Item $zipFile $packageFile
    Remove-Item $stageDir -Recurse -Force
    Write-Host "Packaged $packageFile"
    return
}

if (Test-Path $binDir) { Remove-Item $binDir -Recurse -Force }
dotnet publish $project -c Release -o $binDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $pluginDir"

if ($NoInstall) { return }

$streamDeckExe = 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe'
$dest = Join-Path $env:APPDATA "Elgato\StreamDeck\Plugins\$uuid.sdPlugin"

Get-Process StreamDeck, AppleMusicPlugin -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2  # let Windows release the plugin files

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item $pluginDir $dest -Recurse
Start-Process $streamDeckExe
Write-Host "Installed to $dest and restarted Stream Deck."
