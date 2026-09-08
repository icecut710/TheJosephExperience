# Cloud Updates

This directory hosts the update manifest that the app fetches to check for new versions.

## How it works

1. The app fetches `update-manifest.json` from GitHub raw (master branch)
2. Compares the manifest version against the running version
3. If newer: downloads the ZIP from the `downloadUrl` (GitHub Releases)
4. Verifies SHA-256 integrity
5. Extracts and stages the package
6. Launches `UpdaterHelper.exe` to swap files and restart

## Publishing a new release

```powershell
# 1. Build the release ZIP
.\build-release.ps1 -Version "2.1.0"

# 2. Publish to GitHub Releases (auto-creates manifest + release)
.\publish-github.ps1 -Version "2.1.0" -Notes @"
- New feature X
- Bug fix Y
- Performance improvement Z
"@

# 3. Commit the updated manifest
git add updates/
git commit -m "Update manifest to v2.1.0"
git push
```

## Manifest format

| Field | Description |
|-------|-------------|
| `version` | Semantic version of the release |
| `minimumSupportedVersion` | Oldest version that can auto-update |
| `downloadUrl` | Direct download link (GitHub Release asset) |
| `sha256` | SHA-256 hash of the ZIP for integrity verification |
| `fileSize` | Size in bytes (for progress display) |
| `releaseNotes` | Array of strings shown to the user |
| `mandatory` | If true, user must update to continue |
