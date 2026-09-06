param(
    [string]$Configuration = "Release",
    [switch]$NoClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projDir = Split-Path -Parent $projDir
$csproj = Join-Path $projDir "JosephExperience2.csproj"
$publishDir = Join-Path $projDir "bin\Release\net8.0-windows\publish"

if (-not $NoClean) {
    Write-Output "Cleaning previous build artifacts..."
    Remove-Item -Recurse -Force "$projDir\obj" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$projDir\bin" -ErrorAction SilentlyContinue
}

Write-Output "Building The Joseph Experience 2.0..."
& dotnet publish "$csproj" -c $Configuration -o "$publishDir"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}

Write-Output "Prune unused files from publish output..."
$prune = @("*.pdb", "*.md", "*.xml")
Get-ChildItem -Path $publishDir -Recurse -Include $prune | Remove-Item -Force

Write-Output "Creating zip archive..."
$zipPath = Join-Path $projDir "TheJosephExperience2.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Output "Done! Published to: $publishDir"
Write-Output "Zip archive: $zipPath"
