# Builds Good Job, Joseph! and creates a clean distribution zip.
# Usage: .\build-release.ps1 [-Version "2.0.0"] [-Configuration Release] [-SelfContained]
#
# -SelfContained produces a larger zip that runs without .NET 8 Desktop Runtime installed.
# Without it, the zip is smaller but end users must have .NET 8 Desktop Runtime.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $SelfContained) { throw "Friend releases must be self-contained." }
if (Test-Path -LiteralPath (Join-Path $root "release\TheJosephExperience-v$Version-win-x64.zip")) { throw "Release ZIP already exists; choose a new version." }
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$published = gh release view "v$Version" --repo icecut710/TheJosephExperience --json tagName 2>$null
$ErrorActionPreference = $prevEap
if ($LASTEXITCODE -eq 0) { throw "Published versions are immutable; choose a new version." }
$project = Join-Path $root "GoodJobJoseph\JosephExperience2.csproj"
$distParent = Join-Path ([System.IO.Path]::GetTempPath()) ("JosephDistStage-" + [guid]::NewGuid())

# Clean distribution temp dir
if (Test-Path $distParent) { Remove-Item -LiteralPath $distParent -Recurse -Force }

# Internal publish stage (space-free temp dir)
$pubStage = Join-Path ([System.IO.Path]::GetTempPath()) ("JosephPublishStage-" + [guid]::NewGuid())
if (Test-Path $pubStage) { Remove-Item -LiteralPath $pubStage -Recurse -Force }

  $pubArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $pubStage,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:PublishTrimmed=false",
    "-p:InvariantGlobalization=false",
    "--verbosity", "minimal"
  )
if ($SelfContained) { $pubArgs += "--self-contained", "true", "-p:EnableCompressionInSingleFile=true" }
else { $pubArgs += "--self-contained", "false" }

Write-Host "==> Publishing ($Configuration, $Runtime, self-contained=$SelfContained)..." -ForegroundColor Cyan
& dotnet @pubArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Build updater helper
Write-Host "==> Building updater helper..." -ForegroundColor Cyan
$updaterProject = Join-Path $root "GoodJobJoseph\UpdaterHelper\UpdaterHelper.csproj"
$updaterStage = Join-Path ([System.IO.Path]::GetTempPath()) ("JosephUpdaterPublish-" + [guid]::NewGuid())
if (Test-Path $updaterStage) { Remove-Item -LiteralPath $updaterStage -Recurse -Force }

  $updaterArgs = @(
    "publish", $updaterProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $updaterStage,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:InvariantGlobalization=false",
    "--verbosity", "minimal"
  )
if ($SelfContained) { $updaterArgs += "--self-contained", "true", "-p:EnableCompressionInSingleFile=true" }
else { $updaterArgs += "--self-contained", "false" }

& dotnet @updaterArgs
if ($LASTEXITCODE -ne 0) { throw "updater publish failed ($LASTEXITCODE)" }

# Copy updater into main publish stage
Copy-Item -LiteralPath (Join-Path $updaterStage "JosephExperience.Updater.exe") -Destination $pubStage -Force

# Create clean distribution folder structure:
# The Joseph Experience\
#   JosephExperience.exe
#   JosephExperience.Updater.exe
#   README.txt
#   ...required runtime files...
$distFolder = Join-Path $distParent "The Joseph Experience"
New-Item -ItemType Directory -Path $distFolder -Force | Out-Null

Write-Host "==> Assembling clean distribution folder..." -ForegroundColor Cyan

# Files required for the self-contained app to run
# Copy everything from the publish stage (runtime + app binaries)
Get-ChildItem -LiteralPath $pubStage -Recurse -File -Force | ForEach-Object {
    $relative = $_.FullName.Substring($pubStage.Length + 1)
    $destPath = Join-Path $distFolder $relative
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $destPath -Force
}

# Prune unnecessary files (dev junk that should never ship)
$excludePatterns = @("*.pdb", "*.dbg", "*.testrunconfig")
foreach ($pattern in $excludePatterns) {
    Get-ChildItem -LiteralPath $distFolder -Recurse -Filter $pattern -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }
}

# Remove any .env that might exist (never ship real secrets)
Get-ChildItem -LiteralPath $distFolder -Recurse -Filter ".env*" -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne ".env.example" } | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }

  # Generate README.txt
  $readme = @"
THE JOSEPH EXPERIENCE

1. Extract this entire folder.
2. Double-click JosephExperience.exe.
3. Press Ctrl+Z (default celebrate hotkey) to celebrate.
4. Press F8 to toggle celebration sounds on/off.

Good Job, Joseph!
Version $Version

@ 2026 The Joseph Experience. All rights reserved.
"@
Set-Content -Path (Join-Path $distFolder "README.txt") -Value $readme -Encoding UTF8

# Create the final zip with "The Joseph Experience" as the root folder
$zipName = "TheJosephExperience-v$Version-win-x64.zip"
$zipPath = Join-Path $root "release\$zipName"

# Remove old zip
if (Test-Path $zipPath) { throw "Refusing to overwrite existing release ZIP." }
$zipParent = Split-Path -Parent $zipPath
if (-not (Test-Path $zipParent)) {
    New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
}

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
    # Add directory entries for empty directories
    Get-ChildItem -LiteralPath $distFolder -Recurse -Directory -Force | ForEach-Object {
        $relative = $_.FullName.Substring($distFolder.Length + 1)
        $entryName = "$rootEntryName/$relative".TrimEnd('/') + "/"
        $entryName = $entryName -replace '\\', '/'
        $entry = $zip.CreateEntry($entryName)
    }
}
finally {
    $zip.Dispose()
}

$size = "{0:N2} MB" -f ((Get-Item $zipPath).Length / 1MB)
Write-Host "==> Done. Zip size: $size" -ForegroundColor Green
Write-Host "    Path: $zipPath"

# Clean up internal temp dirs
Remove-Item -LiteralPath $pubStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $updaterStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $distParent -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "==> Temp directories cleaned." -ForegroundColor DarkGray

if ((Test-Path (Join-Path $distFolder ".env")) -and -not (Test-Path (Join-Path $distFolder ".env.example"))) {
    Write-Warning "No .env.example in distribution; users will need to create their own .env."
}