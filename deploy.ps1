# Builds Market Ticker and registers it with Windows, like Visual Studio's "Deploy".
# Requires Developer Mode and the Windows 11 SDK (10.0.26100).
# Usage: .\deploy.ps1 [-Configuration Debug|Release] [-Platform x64|ARM64]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64')] [string] $Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MarketTicker\MarketTicker.csproj'
$rid = "win-$($Platform.ToLowerInvariant())"
$outDir = Join-Path $PSScriptRoot "MarketTicker\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid"
$pkgDir = Join-Path $outDir 'AppPackages\'
$layout = Join-Path $outDir 'AppX'

dotnet build $project -c $Configuration -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxPackageDir=$pkgDir
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$msix = Get-ChildItem $pkgDir -Recurse -Filter '*.msix' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No .msix produced under $pkgDir" }

# Stop the running extension so its files can be replaced; Command Palette restarts it on demand.
Get-Process MarketTicker -ErrorAction SilentlyContinue | Stop-Process -Force

# Register the unpacked package in place (loose layout), as Visual Studio does.
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($msix.FullName, $layout)
foreach ($f in 'AppxSignature.p7x', 'AppxBlockMap.xml', '[Content_Types].xml') {
    Remove-Item -LiteralPath (Join-Path $layout $f) -ErrorAction SilentlyContinue
}

Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceUpdateFromAnyVersion
Get-AppxPackage DarkAce.MarketTicker | Select-Object Name, Version, Status
Write-Host "Deployed. In Command Palette run 'Reload' if the extension was already loaded."
