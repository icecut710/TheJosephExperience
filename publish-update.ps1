# Publishes the production update artifact and manifest.
#
# Primary path: uploads ZIP + manifest to Supabase Storage using the service_role key.
# Fallback:     If Supabase writes fail (anon key is read-only), starts a local
#               HTTP server + localtunnel so the manifest is publicly testable.
#
# Usage: .\publish-update.ps1 [-Version "2.0.0"]
#
# Credentials are read from publish/.env (NOT committed to the zip).

[CmdletBinding()]
param(
    [string]$Version = "2.0.0",
    [string]$EnvFile = "publish\.env"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zipPath = Join-Path $root "release\TheJosephExperience-v$Version-win-x64.zip"

if (-not (Test-Path $zipPath)) {
    Write-Error "Release zip not found: $zipPath. Run build-release.ps1 first."
    exit 1
}

# ---- Load .env (secrets are read at runtime, never embedded in code) ----
$envPath = Join-Path $root $EnvFile
if (-not (Test-Path $envPath)) {
    Write-Error ".env file not found at: $envPath"
    exit 1
}
$envVars = @{}
Get-Content $envPath | ForEach-Object {
    if ($_ -match '^\s*([^#\s=]+)\s*=\s*(.+?)\s*$') {
        $envVars[$matches[1]] = $matches[2]
    }
}
$supabaseUrl = $envVars["SUPABASE_URL"]
$anonKey     = $envVars["SUPABASE_ANON_KEY"]
$publishKey  = $envVars["SUPABASE_PUBLISHABLE_KEY"]
$serviceKey  = $envVars["SUPABASE_SERVICE_ROLE_KEY"]
$bucket      = $envVars["SUPABASE_BUCKET"]

if (-not $supabaseUrl) {
    Write-Error "SUPABASE_URL must be set in $EnvFile"
    exit 1
}

$baseUrl = $supabaseUrl.TrimEnd('/')
$zipName = Split-Path $zipPath -Leaf
$sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$fileSize = (Get-Item $zipPath).Length
$releaseNotes = @(
    "v${Version}: Self-contained single-file build",
    "Hotkey rebinding (F2 celebrate, Shift+3 secondary, F8 cycle sounds, Stop Audio)",
    "Live update checking with manifest-based download + SHA-256 verification",
    "Market range selector and celebration activity graph",
    "CS2 GSI integration improvements"
)

# ---- Build manifest JSON ----
$manifest = @{
    version              = $Version
    minimumSupportedVersion = "2.0.0"
    downloadUrl          = ""
    sha256               = $sha256
    fileSize             = $fileSize
    releaseDateUtc       = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    releaseType          = "release"
    mandatory            = $false
    releaseNotes         = $releaseNotes
}
$artifactsDir = Join-Path $root "artifacts"
if (-not (Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null }
$manifestPath = Join-Path $artifactsDir "manifest-$Version.json"

function Build-Manifest {
    param([string]$DownloadUrl)
    $manifest.downloadUrl = $DownloadUrl
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
    return $manifestJson
}

# ---- Upload helper via Supabase Storage REST API ----
function Upload-ToSupabase {
    param([string]$LocalPath, [string]$ObjectPath, [string]$MimeType, [string]$Key)
    $encodedPath = ($ObjectPath.Split('/') | ForEach-Object { [System.Uri]::EscapeDataString($_) }) -join '/'
    $uploadUrl = "$baseUrl/storage/v1/object/$bucket/$encodedPath"
    Write-Host "==> Uploading $([System.IO.Path]::GetFileName($LocalPath)) -> $bucket/$ObjectPath ..." -ForegroundColor Cyan
    $headers = @{ "apikey" = $Key; "Authorization" = "Bearer $Key" }
    $response = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType $MimeType -InFile $LocalPath -ErrorAction Stop
    Write-Host "    Uploaded. Response: $($response | ConvertTo-Json -Compress)" -ForegroundColor Green
}

# ---- Try Supabase upload (service_role preferred, publishable fallback) ----
$supabaseUploadSuccess = $false
$downloadUrl = "$baseUrl/storage/v1/object/public/$bucket/updates/$zipName"
$manifestUrl = "$baseUrl/storage/v1/object/public/$bucket/updates/update-manifest.json"

# Try service_role key first (full access), then publishable key
$uploadKey = $serviceKey
if (-not $uploadKey) { $uploadKey = $publishKey }

if ($uploadKey) {
    Write-Host "==> Attempting Supabase upload to bucket '$bucket'..." -ForegroundColor Cyan
    try {
        $manifestJson = Build-Manifest -DownloadUrl $downloadUrl
        Upload-ToSupabase -LocalPath $zipPath -ObjectPath "updates/$zipName" -MimeType "application/zip" -Key $uploadKey
        Upload-ToSupabase -LocalPath $manifestPath -ObjectPath "updates/update-manifest.json" -MimeType "application/json" -Key $uploadKey
        $supabaseUploadSuccess = $true
        Write-Host "==> Supabase upload complete." -ForegroundColor Green
    } catch {
        Write-Warning "Supabase upload failed: $_"
    }
} else {
    Write-Warning "No service_role or publishable key found in .env. Skipping Supabase upload."
    Write-Warning "The anon key only has read (SELECT) access on storage.objects."
}

# ---- Fallback: localtunnel for public hosting ----
if (-not $supabaseUploadSuccess) {
    Write-Host "==> Falling back to localtunnel for public hosting..." -ForegroundColor Yellow

    # Ensure manifest has correct download URL (will be updated after tunnel starts)
    $manifestJson = Build-Manifest -DownloadUrl "PENDING"
    Copy-Item -Path $manifestPath -Destination (Join-Path $root "release\update-manifest.json") -Force

    # Start a local HTTP server on port 8080 serving the release directory
    Write-Host "==> Starting local HTTP server on port 8080..." -ForegroundColor Cyan
    $httpServer = Start-Process -FilePath "python" -ArgumentList "-m http.server 8080 --directory `"$root\release`"" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2

    # Start localtunnel to expose the local server publicly
    Write-Host "==> Starting localtunnel (subdomain: joseph-update)..." -ForegroundColor Cyan
    $tunnelProc = Start-Process -FilePath "npx" -ArgumentList "localtunnel --port 8080 --subdomain joseph-update" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 10

    $tunnelUrl = "https://joseph-update.loca.lt"

    # Verify tunnel is up
    $check = curl.exe -s -o NUL -w "%{http_code}" "$tunnelUrl" 2>&1
    if ($check -eq "200") {
        Write-Host "    Tunnel is live at: $tunnelUrl" -ForegroundColor Green
    } else {
        Write-Warning "Tunnel check returned HTTP $check. The subdomain may be taken."
        Write-Warning "Trying without a fixed subdomain..."
        $tunnelProc | Stop-Process -Force -ErrorAction SilentlyContinue
        $tunnelProc = Start-Process -FilePath "npx" -ArgumentList "localtunnel --port 8080" -PassThru -WindowStyle Hidden
        Start-Sleep -Seconds 10
        $tunnelUrl = (curl.exe -s "$tunnelUrl" 2>&1 | Select-String -Pattern 'https://[a-z0-9-]+\.loca\.lt' | Select-Object -First 1).Matches.Value
        if (-not $tunnelUrl) { $tunnelUrl = "https://joseph-update.loca.lt" }
    }

    # Update manifest with the real download URL
    $downloadUrl = "$tunnelUrl/$zipName"
    $manifestUrl = "$tunnelUrl/update-manifest.json"
    $manifestJson = Build-Manifest -DownloadUrl $downloadUrl
    Copy-Item -Path $manifestPath -Destination (Join-Path $root "release\update-manifest.json") -Force

    Write-Host "==> Files are being served via localtunnel." -ForegroundColor Green
}

# ---- Output ----
Write-Host ""
Write-Host "==> Production update published." -ForegroundColor Green
Write-Host "    SHA-256:    $sha256"
Write-Host "    Size:       $fileSize bytes ($([math]::Round($fileSize / 1MB, 2)) MB)"
Write-Host "    Download:   $downloadUrl"
Write-Host "    Manifest:   $manifestUrl"
Write-Host "    Local:      $manifestPath"
Write-Host ""
Write-Host "Manifest URL to configure in the app (Settings > Updates):" -ForegroundColor Yellow
Write-Host "    $manifestUrl"

# Keep the HTTP server running for the session
Write-Host ""
Write-Host "Note: localtunnel and HTTP server will remain running for this session." -ForegroundColor DarkGray
if (-not $supabaseUploadSuccess) {
    Write-Host "For production, add SUPABASE_SERVICE_ROLE_KEY to publish/.env and re-run." -ForegroundColor DarkGray
}
