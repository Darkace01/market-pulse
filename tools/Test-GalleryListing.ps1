<#
.SYNOPSIS
    Checks gallery/<author>/<name> against the Command Palette Extension Gallery's rules.

.DESCRIPTION
    Validates extension.json against the gallery's published JSON schema, checks the id matches
    the folder, and enforces the icon and screenshot limits from the gallery's CONTRIBUTING.md.
    With -StoreProductId (and optionally -Homepage / -AuthorUrl) it first writes those values into
    a copy in -OutDir, which is what the gallery workflow submits. Requires PowerShell 7.
#>
param(
    [string]$Listing = (Join-Path (Split-Path -Parent $PSScriptRoot) 'gallery\darkace\market-pulse'),
    [string]$StoreProductId,
    [string]$Homepage,
    [string]$AuthorUrl,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$schemaUrl = 'https://raw.githubusercontent.com/microsoft/CmdPal-Extensions/main/.github/schemas/extension.schema.json'

if ($OutDir) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    Copy-Item (Join-Path $Listing '*') $OutDir -Recurse -Force
    $Listing = $OutDir
}

$jsonPath = Join-Path $Listing 'extension.json'
$ext = Get-Content $jsonPath -Raw | ConvertFrom-Json
if ($StoreProductId) { ($ext.installSources | Where-Object type -eq 'msstore').id = $StoreProductId }
if ($Homepage) { $ext.homepage = $Homepage }
if ($AuthorUrl) { $ext.author.url = $AuthorUrl }
if ($OutDir) { $ext | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding utf8NoBOM }

$errors = @()

$schema = (Invoke-WebRequest $schemaUrl -UseBasicParsing).Content
$json = Get-Content $jsonPath -Raw
if (-not (Test-Json -Json $json -Schema $schema -ErrorAction SilentlyContinue -ErrorVariable schemaErrors)) {
    $errors += $schemaErrors | ForEach-Object { "schema: $($_.Exception.Message)" }
}

$author = Split-Path -Leaf (Split-Path -Parent $Listing)
$name = Split-Path -Leaf $Listing
if (-not $OutDir -and $ext.id -ne "$author.$name") { $errors += "id '$($ext.id)' must be '$author.$name' to match the folder" }

$icon = Join-Path $Listing $ext.icon
if (-not (Test-Path $icon)) { $errors += "icon '$($ext.icon)' not found" }
elseif ((Get-Item $icon).Length -gt 100KB) { $errors += "icon is over 100 KB" }

$shots = Get-ChildItem (Join-Path $Listing 'screenshots') -File -ErrorAction SilentlyContinue
if ($shots.Count -gt 5) { $errors += "more than 5 screenshots" }
$shots | Where-Object { $_.Length -gt 1MB -or $_.Extension -notmatch '^\.(png|jpe?g)$' } |
    ForEach-Object { $errors += "screenshot $($_.Name) must be a PNG/JPEG under 1 MB" }

$storeId = ($ext.installSources | Where-Object type -eq 'msstore').id
if ($StoreProductId -or $OutDir) {
    if ($storeId -notmatch '^[0-9A-Za-z]{12}$') { $errors += "msstore id '$storeId' is not a Store product ID" }
}
elseif ($storeId -eq 'STORE_PRODUCT_ID') {
    Write-Host "Note: msstore id is still the placeholder; the gallery workflow fills it from STORE_PRODUCT_ID."
}

if ($errors) {
    $errors | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Gallery listing '$($ext.id)' is valid." -ForegroundColor Green
