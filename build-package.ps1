# Builds the Market Pulse release packages: one MSIX per platform, an .msixbundle, and the
# .msixupload that Partner Center (Microsoft Store) accepts. Used by .github/workflows/release.yml
# and runnable locally. Requires the Windows 11 SDK (10.0.26100) for MakeAppx/SignTool.
#
# Usage:
#   .\build-package.ps1 -Version 1.2.3.0
#   .\build-package.ps1 -Version 1.2.3.0 -IdentityName 12345DarkAce.MarketPulse -Publisher "CN=..." -PublisherDisplayName DarkAce
#   .\build-package.ps1 -Version 1.2.3.0 -CertificatePath cert.pfx -CertificatePassword ****   # also writes a signed sideload bundle
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+\.0$')] [string] $Version,
    [string[]] $Platforms = @('x64', 'ARM64'),
    [string] $Configuration = 'Release',
    # Store identity from Partner Center (Product identity page). Empty keeps the manifest's values.
    [string] $IdentityName,
    [string] $Publisher,
    [string] $PublisherDisplayName,
    [string] $CertificatePath,
    [string] $CertificatePassword,
    [string] $OutDir = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MarketTicker\MarketTicker.csproj'
$manifestPath = Join-Path $PSScriptRoot 'MarketTicker\Package.appxmanifest'
$name = 'MarketPulse'

function Find-SdkTool([string]$tool) {
    $root = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $found = Get-ChildItem $root -Recurse -Filter $tool -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $found) { throw "$tool not found under $root. Install the Windows 11 SDK." }
    return $found.FullName
}

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$msixDir = New-Item -ItemType Directory -Force (Join-Path $OutDir 'msix')

# Stamp version and (optionally) the Store identity into the manifest; restored afterwards.
$originalManifest = [IO.File]::ReadAllText($manifestPath)
try {
    [xml]$xml = $originalManifest
    $xml.Package.Identity.Version = $Version
    if ($IdentityName) { $xml.Package.Identity.Name = $IdentityName }
    if ($Publisher) { $xml.Package.Identity.Publisher = $Publisher }
    if ($PublisherDisplayName) { $xml.Package.Properties.PublisherDisplayName = $PublisherDisplayName }
    $xml.Save($manifestPath)

    foreach ($platform in $Platforms) {
        $pkgDir = Join-Path $OutDir "build\$platform\"
        Write-Host "=== Building $platform ($Configuration, $Version)" -ForegroundColor Cyan
        dotnet build $project -c $Configuration -p:Platform=$platform `
            -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
            -p:AppxBundle=Never -p:AppxPackageDir=$pkgDir
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $platform" }

        $msix = Get-ChildItem $pkgDir -Recurse -Filter '*.msix' | Select-Object -First 1
        if (-not $msix) { throw "No .msix produced for $platform" }
        Copy-Item $msix.FullName (Join-Path $msixDir "${name}_${Version}_$platform.msix")
    }
}
finally {
    [IO.File]::WriteAllText($manifestPath, $originalManifest)
}

$makeAppx = Find-SdkTool 'makeappx.exe'
$bundle = Join-Path $OutDir "${name}_$Version.msixbundle"
& $makeAppx bundle /d $msixDir /p $bundle /bv $Version /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx bundle failed" }

# An .msixupload is a zip holding the bundle; it is what Partner Center and `msstore publish -i` take.
$upload = Join-Path $OutDir "${name}_$Version.msixupload"
Compress-Archive -Path $bundle -DestinationPath "$upload.zip" -Force
Move-Item "$upload.zip" $upload -Force

if ($CertificatePath) {
    $signTool = Find-SdkTool 'signtool.exe'
    $signed = Join-Path $OutDir "${name}_${Version}_signed.msixbundle"
    Copy-Item $bundle $signed
    & $signTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword /tr http://timestamp.digicert.com /td SHA256 $signed
    if ($LASTEXITCODE -ne 0) { throw "Signing failed" }
}

Remove-Item (Join-Path $OutDir 'build') -Recurse -Force
Write-Host "Packages in ${OutDir}:" -ForegroundColor Green
Get-ChildItem $OutDir -Recurse -File | ForEach-Object { "  {0} ({1:N1} MB)" -f $_.FullName.Substring($OutDir.Length + 1), ($_.Length / 1MB) }
