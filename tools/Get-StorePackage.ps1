<#
.SYNOPSIS
    Fetches the Store-signed Market Pulse package, attaches it to the GitHub release, and starts
    the WinGet submission.

.DESCRIPTION
    WinGet installs the exact package the Microsoft Store signed, so both channels share one
    identity and users never need a certificate. Downloading it needs an interactive Microsoft
    Entra ID sign-in (winget's rule for Store packages), which is why this runs on your machine
    rather than in CI. Run it after the Store has certified and published the version.

    Steps: winget download from msstore -> check the Microsoft Marketplace signature and version ->
    gh release upload v<version> -> gh workflow run winget.yml.

.EXAMPLE
    .\tools\Get-StorePackage.ps1 -Version 1.2.0
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string]$Version,
    # Defaults to the repository variable STORE_PRODUCT_ID.
    [string]$ProductId,
    [switch]$NoSubmit
)

$ErrorActionPreference = 'Stop'
$packageVersion = "$Version.0"
$tag = "v$Version"

if (-not $ProductId) { $ProductId = (gh variable get STORE_PRODUCT_ID).Trim() }
if (-not $ProductId) { throw "Set the STORE_PRODUCT_ID repository variable or pass -ProductId." }

$work = Join-Path ([IO.Path]::GetTempPath()) "marketpulse-store-$Version"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null

Write-Host "Downloading $ProductId from the Microsoft Store (sign in with your Microsoft Entra ID account if asked)..."
winget download --id $ProductId --source msstore --skip-license --skip-dependencies `
    --download-directory $work --accept-source-agreements --accept-package-agreements
if ($LASTEXITCODE -ne 0) { throw "winget download failed ($LASTEXITCODE)" }

$files = Get-ChildItem $work -Recurse -File | Where-Object Extension -in '.msixbundle', '.msix', '.appxbundle', '.appx'
$bundle = $files | Where-Object Extension -in '.msixbundle', '.appxbundle' | Select-Object -First 1
$packages = if ($bundle) { @($bundle) } else { @($files) }
if (-not $packages) { throw "No package found in $work" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$assets = foreach ($pkg in $packages) {
    $sig = Get-AuthenticodeSignature $pkg.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Issuer -notmatch 'Microsoft Marketplace') {
        throw "$($pkg.Name) is not Store-signed (status $($sig.Status), issuer $($sig.SignerCertificate.Issuer))"
    }

    $zip = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $isBundle = [bool]$zip.GetEntry('AppxMetadata/AppxBundleManifest.xml')
        $entry = $zip.GetEntry($(if ($isBundle) { 'AppxMetadata/AppxBundleManifest.xml' } else { 'AppxManifest.xml' }))
        $reader = New-Object IO.StreamReader($entry.Open())
        [xml]$manifest = $reader.ReadToEnd()
        $reader.Dispose()
    }
    finally { $zip.Dispose() }

    $identity = if ($isBundle) { $manifest.Bundle.Identity } else { $manifest.Package.Identity }
    if ($identity.Version -ne $packageVersion) {
        throw "The Store is serving $($identity.Version), not $packageVersion. Wait until the update is published, then retry."
    }

    $suffix = if ($isBundle) { '' } else { "_$($identity.ProcessorArchitecture)" }
    $name = "MarketPulse_${packageVersion}_store$suffix$($pkg.Extension.ToLowerInvariant())"
    $dest = Join-Path $work $name
    Move-Item $pkg.FullName $dest
    $dest
}

Write-Host "Attaching to release ${tag}:" -ForegroundColor Cyan
$assets | ForEach-Object { "  $(Split-Path -Leaf $_)" }
gh release upload $tag @assets --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }

if (-not $NoSubmit) {
    gh workflow run winget.yml -f version=$Version -f dry_run=false
    if ($LASTEXITCODE -ne 0) { throw "Could not start the WinGet workflow" }
    Write-Host "Started the WinGet workflow; follow it with: gh run watch" -ForegroundColor Green
}
