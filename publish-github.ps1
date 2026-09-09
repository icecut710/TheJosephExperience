# Publishes a release to GitHub Releases for cloud updates.
#
# Prerequisites:
#   - GitHub CLI (gh) installed and authenticated
#   - Run build-release.ps1 first to produce the ZIP
#
# Usage: .\publish-github.ps1 [-Version "2.1.0"] [-Notes "release notes here"]
#
# What it does:
#   1. Computes SHA-256 of the release ZIP
#   2. Writes updates/update-manifest.json (committed to master)
#   3. Creates a GitHub Release with the ZIP attached
#   4. The app fetches the manifest from GitHub raw, downloads the ZIP from the release

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = "See CHANGELOG for details."
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repo = "icecut710/TheJosephExperience"

# ---- Find the release ZIP ----
$zipName = "TheJosephExperience-v$Version-win-x64.zip"
$zipPath = Join-Path $root "release\$zipName"
if (-not (Test-Path $zipPath)) {
    Write-Error "Release zip not found: $zipPath`nRun build-release.ps1 -Version $Version first."
    exit 1
}

# ---- Compute SHA-256 ----
$sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
$fileSize = (Get-Item $zipPath).Length
Write-Host "SHA-256: $sha256" -ForegroundColor Cyan
Write-Host "Size:    $fileSize bytes" -ForegroundColor Cyan

# ---- Verify GitHub CLI is available ----
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "`n⚠ GitHub CLI not found. Install from: https://cli.github.com/" -ForegroundColor Yellow
    Write-Host "Then run: gh auth login" -ForegroundColor Yellow
    exit 1
}

# Never change an already-published version.
$existing = gh release view "v$Version" --repo $repo --json tagName 2>$null
if ($LASTEXITCODE -eq 0) { throw "Release v$Version already exists; choose a new version." }

# ---- Create the release ----
Write-Host "`nCreating GitHub Release v$Version..." -ForegroundColor Cyan
$releaseNotesFile = Join-Path $env:TEMP "release-notes.md"
$Notes | Set-Content -Path $releaseNotesFile -Encoding UTF8

gh release create "v$Version" `
    --repo $repo `
    --title "v$Version" `
    --notes-file $releaseNotesFile `
    $zipPath

if ($LASTEXITCODE -eq 0) {
$verifyDir = Join-Path $env:TEMP ("JosephReleaseVerify-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $verifyDir | Out-Null
gh release download "v$Version" --repo $repo --pattern $zipName --dir $verifyDir
if ($LASTEXITCODE -ne 0) { throw "Release download verification failed; manifest unchanged." }
if ((Get-FileHash -LiteralPath (Join-Path $verifyDir $zipName)).Hash.ToLowerInvariant() -ne $sha256) {
    throw "Published asset SHA-256 mismatch; manifest unchanged."
}
# ---- Ensure updates/ directory exists ----
$updatesDir = Join-Path $root "updates"
if (-not (Test-Path $updatesDir)) {
    New-Item -ItemType Directory -Path $updatesDir | Out-Null
}

# ---- Write the manifest (committed to master, served via raw.githubusercontent) ----
$manifest = @{
    version               = $Version
    minimumSupportedVersion = "2.0.0"
    downloadUrl           = "https://github.com/$repo/releases/download/v$Version/$zipName"
    mandatory             = $false
    releaseDateUtc        = (Get-Date).ToUniversalTime().ToString("o")
    releaseType           = "release"
    sha256                = $sha256
    fileSize              = $fileSize
    releaseNotes          = @($Notes -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
} | ConvertTo-Json -Depth 5

$manifestPath = Join-Path $updatesDir "update-manifest.json"
$manifest | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "`nManifest written to: $manifestPath" -ForegroundColor Green


    Write-Host "`n✅ Release v$Version published!" -ForegroundColor Green
    Write-Host "   Manifest: https://raw.githubusercontent.com/$repo/master/updates/update-manifest.json" -ForegroundColor Gray
    Write-Host "   Release:  https://github.com/$repo/releases/tag/v$Version" -ForegroundColor Gray
    Write-Host "`n   Commit the updated manifest to master:" -ForegroundColor Yellow
    Write-Host "   git add updates/ && git commit -m 'Update manifest to v$Version' && git push" -ForegroundColor White
} else {
    throw "Release creation failed; manifest was not changed."
}
