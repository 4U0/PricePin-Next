# Builds the Stream Deck plugin and packs it as PricePin.streamDeckPlugin (a zip of the
# com.pricepin.streamdeck.sdPlugin folder). Double-clicking that file installs it in Stream Deck.
# Usage: pwsh tools/pack-streamdeck.ps1 -Version 1.7.0 -Out releases
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Out = "releases"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path ([System.IO.Path]::GetTempPath()) "pricepin-streamdeck"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$plugin = Join-Path $work "com.pricepin.streamdeck.sdPlugin"

# x64 also runs on ARM64 Windows through emulation, so one build covers both.
dotnet publish src/PricePin.StreamDeck/PricePin.StreamDeck.csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $plugin
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Stream Deck wants a four-part version; pre-release suffixes are dropped.
$numeric = ($Version -replace '-.*$', '')
$manifestPath = Join-Path $plugin "manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.Version = "$numeric.0"
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding utf8

foreach ($required in 'PricePin.StreamDeck.exe', 'manifest.json', 'pi/index.html', 'imgs/plugin.png') {
    if (-not (Test-Path (Join-Path $plugin $required))) { throw "Plugin folder is missing $required" }
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$target = Join-Path (Resolve-Path $Out) "PricePin.streamDeckPlugin"
if (Test-Path $target) { Remove-Item $target -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($plugin, $target, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Get-Item $target | Select-Object Name, Length
