<#
.SYNOPSIS
    Writes the WinGet manifests (version, installer, en-US locale) for a Market Pulse release.

.DESCRIPTION
    Reads the packages that the release ships (the Store-signed .msixbundle or per-arch .msix) to fill in
    InstallerSha256, SignatureSha256 and PackageFamilyName, then writes
    <OutDir>/manifests/d/DarkAce/MarketPulse/<version>/, the layout microsoft/winget-pkgs expects.
    The Command Palette finds extensions on WinGet by the windows-commandpalette-extension tag.

.EXAMPLE
    .\tools\New-WinGetManifest.ps1 -Version 1.2.0.0 -PackagePath .\MarketPulse_1.2.0.0_store.msixbundle `
        -InstallerUrl https://github.com/owner/repo/releases/download/v1.2.0/MarketPulse_1.2.0.0_store.msixbundle `
        -RepositoryUrl https://github.com/owner/repo
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string]$Version,
    # The Store-signed .msixbundle, or one .msix per architecture, each with its download URL.
    [Parameter(Mandatory)] [string[]]$PackagePath,
    [Parameter(Mandatory)] [string[]]$InstallerUrl,
    [Parameter(Mandatory)] [string]$RepositoryUrl,
    [string]$PackageIdentifier = 'DarkAce.MarketPulse',
    [string]$Publisher = 'DarkAce',
    [string]$License = 'Proprietary',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\winget')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Package family name = Name + '_' + publisher hash: the first 8 bytes of SHA-256 over the UTF-16LE
# publisher, read as 65 bits (zero-padded) in 5-bit groups with Windows' base32 alphabet.
function Get-PublisherId([string]$publisher) {
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::Unicode.GetBytes($publisher))
    $bits = -join ($hash[0..7] | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') }) + '0'
    $alphabet = '0123456789abcdefghjkmnpqrstvwxyz'
    return -join (0..12 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
}

function Read-ZipEntry([IO.Compression.ZipArchive]$zip, [string]$name) {
    $entry = $zip.GetEntry($name)
    if (-not $entry) { return $null }
    $stream = $entry.Open()
    try { $ms = New-Object IO.MemoryStream; $stream.CopyTo($ms); return $ms.ToArray() } finally { $stream.Dispose() }
}

if ($PackagePath.Count -ne $InstallerUrl.Count) { throw "Pass one -InstallerUrl per -PackagePath" }

# One installer entry per architecture each package covers (a bundle covers several).
$entries = @()
$familyName = $null
for ($i = 0; $i -lt $PackagePath.Count; $i++) {
    $path = (Resolve-Path $PackagePath[$i]).Path
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $isBundle = [bool]$zip.GetEntry('AppxMetadata/AppxBundleManifest.xml')
        $manifestEntry = if ($isBundle) { 'AppxMetadata/AppxBundleManifest.xml' } else { 'AppxManifest.xml' }
        [xml]$manifest = [Text.Encoding]::UTF8.GetString((Read-ZipEntry $zip $manifestEntry)).TrimStart([char]0xFEFF)
        $signature = Read-ZipEntry $zip 'AppxSignature.p7x'
    }
    finally {
        $zip.Dispose()
    }

    $identity = if ($isBundle) { $manifest.Bundle.Identity } else { $manifest.Package.Identity }
    if ($identity.Version -ne $Version) { throw "$path is version $($identity.Version), not $Version" }
    if (-not $signature) { throw "$path is not signed. WinGet needs the Store-signed package (see tools\Get-StorePackage.ps1)." }

    $pfn = "$($identity.Name)_$(Get-PublisherId $identity.Publisher)"
    if ($familyName -and $pfn -ne $familyName) { throw "Packages have different identities ($familyName, $pfn)" }
    $familyName = $pfn

    $archs = if ($isBundle) {
        $manifest.Bundle.Packages.Package | Where-Object Type -eq 'application' | ForEach-Object { $_.Architecture.ToLowerInvariant() }
    } else {
        @($identity.ProcessorArchitecture.ToLowerInvariant())
    }
    foreach ($arch in $archs | Sort-Object -Unique) {
        $entries += [pscustomobject]@{
            Architecture = $arch
            Url          = $InstallerUrl[$i]
            Sha          = (Get-FileHash $path -Algorithm SHA256).Hash
            SignatureSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($signature))
        }
    }
}
$architectures = $entries.Architecture

$dir = Join-Path $OutDir ("manifests\{0}\{1}\{2}\{3}" -f $PackageIdentifier.Substring(0, 1).ToLowerInvariant(), ($PackageIdentifier -split '\.')[0], ($PackageIdentifier -split '\.', 2)[1], $Version)
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Force $dir | Out-Null
$schema = '1.10.0'
$header = "# Created by Market Pulse tools/New-WinGetManifest.ps1"

$installers = ($entries | Sort-Object Architecture -Unique | ForEach-Object {
@"
- Architecture: $($_.Architecture)
  InstallerUrl: $($_.Url)
  InstallerSha256: $($_.Sha)
  SignatureSha256: $($_.SignatureSha)
"@
}) -join "`n"

@"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json

PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$PackageIdentifier.yaml") -Encoding utf8NoBOM

@"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json

PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.19041.0
InstallerType: msix
PackageFamilyName: $familyName
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.PowerToys
Installers:
$installers
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$PackageIdentifier.installer.yaml") -Encoding utf8NoBOM

@"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json

PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
PackageLocale: en-US
Publisher: $Publisher
PublisherUrl: $(($RepositoryUrl -split '/')[0..3] -join '/')
PublisherSupportUrl: $RepositoryUrl/issues
PackageName: Market Pulse
PackageUrl: $RepositoryUrl
License: $License
ShortDescription: Live stock, index, FX, commodity and crypto prices on the PowerToys Command Palette Dock.
Description: |-
  Market Pulse puts live market prices on the PowerToys Command Palette Dock.
  Stocks, ETFs, indices, FX pairs and futures come from Yahoo Finance; crypto is streamed live from Binance.
  Pin a single market, a whole group, every market in one strip, or a rotating ticker. No API key needed.
Moniker: market-pulse
Tags:
- windows-commandpalette-extension
- powertoys
- command-palette
- stocks
- crypto
- forex
- markets
ReleaseNotesUrl: $RepositoryUrl/releases/tag/v$($Version -replace '\.0$', '')
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$PackageIdentifier.locale.en-US.yaml") -Encoding utf8NoBOM

Write-Host "WinGet manifests for $PackageIdentifier $Version ($familyName, $($architectures -join '/')):" -ForegroundColor Green
Get-ChildItem $dir | ForEach-Object { "  $($_.FullName)" }
$dir
