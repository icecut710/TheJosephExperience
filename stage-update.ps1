# Stages a cloud update candidate without making it live.
# Usage: .\stage-update.ps1 [-Version "2.0.0"] [-NotesFile "release-notes.md"] [-SelfContained]
#
# Creates: artifacts/update-staging/<version>/
#   - TheJosephExperience-v<version>-win-x64.zip (release zip with "The Joseph Experience" folder)
#   - update-manifest.json
#   - SHA256SUMS.txt
#   - RELEASE_NOTES.md
#   - BUILD_INFO.txt
#
# Does NOT upload to any remote, change any live manifest, or make the update discoverable.

[CmdletBinding()]
param(
    [string]$Version = "2.0.0",
    [string]$NotesFile = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "GoodJobJoseph\JosephExperience2.csproj"
$releaseDir = Join-Path $root "release"
$stageDir = Join-Path $root "artifacts\update-staging\$Version"

# Clean/create staging directory
if (Test-Path $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# Internal publish stage (space-free temp dir)
$pubStage = Join-Path ([System.IO.Path]::GetTempPath()) "JosephPublishStage"
if (Test-Path $pubStage) { Remove-Item -LiteralPath $pubStage -Recurse -Force }

$pubArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $pubStage,
    "-p:PublishSingleFile=false",
    "-p:DebugType=none",
    "--verbosity", "minimal"
)
if ($SelfContained) { $pubArgs += "--self-contained", "true" } else { $pubArgs += "--self-contained", "false" }

Write-Host "==> Publishing v$Version (self-contained=$SelfContained)..." -ForegroundColor Cyan
& dotnet @pubArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Build updater helper
Write-Host "==> Building updater helper..." -ForegroundColor Cyan
$updaterProject = Join-Path $root "GoodJobJoseph\UpdaterHelper\UpdaterHelper.csproj"
$updaterStage = Join-Path ([System.IO.Path]::GetTempPath()) "JosephUpdaterPublish"
if (Test-Path $updaterStage) { Remove-Item -LiteralPath $updaterStage -Recurse -Force }

$updaterArgs = @(
    "publish", $updaterProject,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $updaterStage,
    "-p:DebugType=none",
    "--verbosity", "minimal"
)
if ($SelfContained) { $updaterArgs += "--self-contained", "true" } else { $updaterArgs += "--self-contained", "false" }

& dotnet @updaterArgs
if ($LASTEXITCODE -ne 0) { throw "updater publish failed ($LASTEXITCODE)" }

# Copy updater executable into main publish stage
Copy-Item -LiteralPath (Join-Path $updaterStage "JosephExperience.Updater.exe") -Destination $pubStage -Force

# Create clean distribution folder
$distParent = Join-Path ([System.IO.Path]::GetTempPath()) "JosephDistStage"
if (Test-Path $distParent) { Remove-Item -LiteralPath $distParent -Recurse -Force }
$distFolder = Join-Path $distParent "The Joseph Experience"
New-Item -ItemType Directory -Path $distFolder -Force | Out-Null

# Copy all publish output into distribution folder
Get-ChildItem -LiteralPath $pubStage -Recurse -File -Force | ForEach-Object {
    $relative = $_.FullName.Substring($pubStage.Length + 1)
    $destPath = Join-Path $distFolder $relative
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $destPath -Force
}

# Prune dev junk
Get-ChildItem -LiteralPath $distFolder -Recurse -Filter "*.pdb" -Force | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
Get-ChildItem -LiteralPath $distFolder -Recurse -Filter "*.dbg" -Force -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

# Remove any real .env (keep only .env.example)
Get-ChildItem -LiteralPath $distFolder -Recurse -Filter ".env*" -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne ".env.example" } | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

# README.txt
$readme = @"
THE JOSEPH EXPERIENCE

1. Extract this entire folder.
2. Double-click JosephExperience.exe.
3. Press F2 to celebrate.
4. Press Shift+3 for a secondary celebration.
5. Press F8 to cycle celebration sounds.
6. Press the Stop Audio hotkey (default: Shift+S) to stop current audio.

The app runs locally and may use Windows Game State Integration for optional
Counter-Strike 2 support. Configure CS2 integration in Settings > Games.

Do not move individual DLL files out of this folder.

@ 2026 The Joseph Experience. All rights reserved.
"@
Set-Content -Path (Join-Path $distFolder "README.txt") -Value $readme -Encoding UTF8

# Create the release zip (preserves "The Joseph Experience" top-level folder)
$zipName = "TheJosephExperience-v$Version-win-x64.zip"
$zipPath = Join-Path $stageDir $zipName

Write-Host "==> Creating zip at $zipPath ..." -ForegroundColor Cyan
Add-Type -AssemblyName "System.IO.Compression"
Add-Type -AssemblyName "System.IO.Compression.FileSystem"

# Create zip with "The Joseph Experience" as the root entry in the archive
$rootEntryName = "The Joseph Experience"
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $distFolder -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($distFolder.Length + 1)
        $entryName = "$rootEntryName/$relative" -replace '\\', '/'
        $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $_.LastWriteTime
        $entryStream = $entry.Open()
        $fileStream = [System.IO.File]::OpenRead($_.FullName)
        $fileStream.CopyTo($entryStream)
        $fileStream.Dispose()
        $entryStream.Dispose()
    }
}
finally {
    $zip.Dispose()
}

# Also update the main release zip
if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null }
$mainZip = Join-Path $releaseDir "TheJosephExperience.zip"
if (Test-Path $mainZip) { Remove-Item -LiteralPath $mainZip -Force }
Copy-Item -LiteralPath $zipPath -Destination $mainZip

# Calculate SHA-256
$sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# Write SHA256SUMS.txt
"SHA-256: $sha256" | Set-Content -Path (Join-Path $stageDir "SHA256SUMS.txt") -Encoding UTF8

# Write update-manifest.json
if ($NotesFile -and (Test-Path $NotesFile)) {
    $releaseNotes = (Get-Content $NotesFile -Raw).Trim() -split "`n" | ForEach-Object { $_ -replace '"', '' }
} else {
    $releaseNotes = @(
        "Build $Version`: CS2 GSI integration, UI refinements, bug fixes."
        "GSI port standardized to 3000."
        "Old config migration implemented."
        "Bomb exploded event added."
        "Cloud update staging pipeline complete."
    )
}
$manifest = @{
    version = $Version
    minimumSupportedVersion = "2.0.0"
    downloadUrl = "https://example.com/updates/$zipName"
    sha256 = $sha256
    fileSize = (Get-Item $zipPath).Length
    releaseDateUtc = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')
    releaseType = "release"
    mandatory = $false
    releaseNotes = $releaseNotes
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $stageDir "update-manifest.json") -Value $manifestJson -Encoding UTF8

# Write BUILD_INFO.txt
$bInfo = @"
Build Information
=================
Version:         $Version
Configuration:   Release
Runtime:         win-x64
Self-contained:   $($SelfContained.IsPresent)
Built:           $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Project:         JosephExperience2.csproj

MSBuild Properties:
  -p:PublishSingleFile=false
  -p:DebugType=none

Output:
  Zip:        $zipName ($([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)
  SHA-256:    $sha256
"@
Set-Content -Path (Join-Path $stageDir "BUILD_INFO.txt") -Value $bInfo -Encoding UTF8

# Write RELEASE_NOTES.md
$releaseNotes = @"
# The Joseph Experience 2.0 - v$Version

## Release

**Date:** $(Get-Date -Format 'yyyy-MM-dd')
**Status:** Staged
**Self-contained:** $($SelfContained.IsPresent)

### Changes

- CS2 GSI port standardized to 3000
- Old Good Job, Joseph! GSI config migration implemented
- Bomb exploded event support added
- Cloud update staging pipeline complete
- Single-instance mutex: JosephExperience.SingleInstance.5A3B7C9D
- HotkeyService reliability fixes
- Build script fixed for paths with spaces
- Market view with real NADD/SOL token data from GeckoTerminal
- Celebration activity graph from SQLite history
"@
Set-Content -Path (Join-Path $stageDir "RELEASE_NOTES.md") -Value $releaseNotes -Encoding UTF8

Write-Host "==> Staging complete." -ForegroundColor Green
Write-Host "    Artifact: $stageDir"
Write-Host "    Zip: $zipName ($([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)"
Write-Host "    SHA-256: $sha256"

# Clean up temp dirs
Remove-Item -LiteralPath $pubStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $updaterStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $distParent -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "==> Temp directories cleaned." -ForegroundColor DarkGray