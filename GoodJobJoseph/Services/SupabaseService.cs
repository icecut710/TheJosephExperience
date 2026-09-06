using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

public enum SupabaseState
{
    NotConfigured,
    Connecting,
    Connected,
    Syncing,
    Offline,
    Error
}

public class SupabaseSyncResult
{
    public bool Success { get; set; }
    public int Downloaded { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int Reconciled { get; set; }
    public int Deactivated { get; set; }
    public int RemoteRows { get; set; }
    public string? Error { get; set; }
    public string? DisplayName { get; set; }
}

public class SupabaseConfig
{
    public string Url { get; set; } = "";
    public string AnonKey { get; set; } = "";
        public string Bucket { get; set; } = "good-job-joseph-images";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(AnonKey)
        && !Url.StartsWith("your-suppabase", StringComparison.OrdinalIgnoreCase)
        && !AnonKey.StartsWith("your-anon", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Remote canonical image library backed by Supabase.
///
/// Postgres table <c>public.celebration_images</c> is the single canonical source of
/// truth; Storage bucket <c>joseph-images</c> holds the actual images. The desktop
/// client reads <c>enabled</c> and <c>deleted_at IS NULL</c> rows via the anon key
/// (read-only, RLS-restricted). Downloaded objects are cached under the library cache
/// directory. Reconciliation deactivates any local cloud mirror whose remote row is
/// missing/deleted so deleted assets never cycle back as blank entries.
///
/// Authentication is read-only and never embeds privileged secrets. Configuration
/// values are stored in a .env next to the binary or in the data directory.
/// </summary>
public class SupabaseService : IDisposable
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly string _dataDir;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Timer? _periodicTimer;

    public SupabaseConfig Config { get; private set; } = new();
    public SupabaseState State { get; private set; } = SupabaseState.NotConfigured;
    public DateTime? LastSyncUtc { get; private set; }
    public DateTime? LastSuccessUtc { get; private set; }
    public int LastRemoteRows { get; private set; }
    public int LastDownloaded { get; private set; }
    public int LastReconciled { get; private set; }
    public bool SyncInProgress { get; private set; }
    public int SyncFailuresInRow { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    /// <summary>True only after at least one successful authenticated request recently.</summary>
    public bool IsConnected => State == SupabaseState.Connected || State == SupabaseState.Syncing;

    public string StateText => State switch
    {
        SupabaseState.NotConfigured => "NOT CONFIGURED",
        SupabaseState.Connecting => "CONNECTING",
        SupabaseState.Connected => "CONNECTED",
        SupabaseState.Syncing => "SYNCING",
        SupabaseState.Offline => "OFFLINE",
        SupabaseState.Error => "ERROR",
        _ => "UNKNOWN"
    };

    public SupabaseService(string dataDir)
    {
        _dataDir = dataDir;
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, ".env");
            var dataEnv = Path.Combine(dataDir, ".env");
            var envFile = File.Exists(bundled) ? bundled : (File.Exists(dataEnv) ? dataEnv : bundled);
            if (File.Exists(envFile))
            {
                Config = ParseEnv(File.ReadAllLines(envFile));
            }
        }
        catch
        {
            Config = new SupabaseConfig();
        }

        // State already initialized to NotConfigured at line 74; no further action needed.
    }

    private static SupabaseConfig ParseEnv(string[] lines)
    {
        var cfg = new SupabaseConfig();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var idx = line.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            switch (key)
            {
                case "SUPABASE_URL" when value.Length > 0: cfg.Url = value.TrimEnd('/'); break;
                case "SUPABASE_ANON_KEY" when value.Length > 0: cfg.AnonKey = value; break;
                case "SUPABASE_PUBLISHABLE_KEY" when value.Length > 0: cfg.AnonKey = value; break;
                case "SUPABASE_BUCKET" when value.Length > 0: cfg.Bucket = value; break;
            }
        }
        return cfg;
    }

    public string StatusText
    {
        get
        {
            if (!Config.IsConfigured)
            {
                return "Add a .env to enable cloud sync.";
            }
            if (State == SupabaseState.Offline)
            {
                return string.IsNullOrEmpty(LastError) ? "Offline — using cached Josephs." : $"Offline — {LastError}";
            }
            if (State == SupabaseState.Syncing)
            {
                return "Syncing…";
            }
            if (State == SupabaseState.Connected)
            {
                if (LastSyncUtc is null)
                {
                    return "Connected (never synced)";
                }
                var delta = DateTime.UtcNow - LastSyncUtc.Value;
                return delta.TotalMinutes < 1 ? "Synced just now"
                    : delta.TotalMinutes < 60 ? $"Synced {Math.Max(1, (int)delta.TotalMinutes)}m ago"
                    : $"Synced {(int)delta.TotalHours}h ago";
            }
            return StateText;
        }
    }

    /// <summary>Diagnostics summary. Never includes secrets.</summary>
    public string DiagnosticsSummary
    {
        get
        {
            var url = Config.Url;
            var host = "not set";
            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                host = u.Host;
            }
            return $"configured: {(Config.IsConfigured ? "YES" : "NO")}; project: {host}; "
                 + $"key present: {(Config.IsConfigured ? "YES" : "NO")}; "
                 + $"state: {StateText}; remote rows: {LastRemoteRows}; downloaded: {LastDownloaded}; "
                 + $"reconciled: {LastReconciled}; last sync: {LastSyncUtc?.ToString("s") ?? "never"}";
        }
    }

    /// <summary>
    /// Tests connectivity with a real authenticated catalog request. Updates
    /// State truthfully (CONNECTED / OFFLINE / ERROR / NOT CONFIGURED).
    /// </summary>
    public async Task<SupabaseSyncResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var result = new SupabaseSyncResult();
        if (!Config.IsConfigured)
        {
            State = SupabaseState.NotConfigured;
            result.Error = "Supabase is not configured. Add a .env file (see .env.example).";
            return result;
        }

        State = SupabaseState.Connecting;
        try
        {
            // Real authenticated catalog request (with schema fallback). Any
            // failure throws and is reported truthfully below.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            await FetchCatalogAsync(1, cts.Token).ConfigureAwait(false);

            LastSuccessUtc = DateTime.UtcNow;
            State = SupabaseState.Connected;
            AppLog.Info("Supabase: connection verified (catalog OK).");
            result.Success = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            State = SupabaseState.Offline;
            LastError = "Connection timed out.";
            result.Error = LastError;
            return result;
        }
        catch (Exception ex)
        {
            State = SupabaseState.Offline;
            LastError = ex.Message;
            result.Error = LastError;
            AppLog.Warn($"Supabase: test connection error: {ex.Message}");
            return result;
        }
    }

private static string DescribeHttpError(int status)
    {
        return status switch
        {
            400 => $"Bad request (400) — the catalog query may be invalid.",
            401 => "Unauthorized (401) — the API key is invalid or missing.",
            403 => "Forbidden (403) — check RLS policies on celebration_images.",
            404 => "Not found (404) — the catalog query may be invalid.",
            >= 500 and <= 599 => $"Server error ({status}) — Supabase is having trouble.",
            _ => $"HTTP {status}."
        };
    }

    private string DescribeUploadError(int status, string path)
    {
        return status switch
        {
            400 => "Bad request — the upload was rejected by the server.",
            401 => "Unauthorized — the API key is invalid or missing.",
            403 => "Forbidden — check RLS policies on storage or celebration_images.",
            404 => "Not found — the bucket may not exist.",
            >= 500 and <= 599 => "Server error — the cloud service is having trouble.",
            _ => "HTTP upload error."
        };
    }

    /// <summary>
    /// Catalog select variants, tried in order. The first matches the shipped
    /// schema (supabase/setup_josephexperience.sql); later variants degrade
    /// gracefully so the client still syncs against older/simpler remote tables
    /// (PostgREST returns 400 if a requested column does not exist).
    /// </summary>
    private static readonly string[] CatalogSelectVariants =
    {
        "id,display_name,storage_path,category,tags,enabled,favorite,weight,sha256,mime_type,file_size,created_at,updated_at",
        "id,display_name,storage_path,category,tags,enabled,favorite,weight,sha256,created_at,updated_at",
        "id,display_name,storage_path,enabled,updated_at",
        "id,display_name,storage_path"
    };

    private HttpRequestMessage BuildCatalogRequest(int limit, string select)
    {
        var url = $"{Config.Url}/rest/v1/celebration_images"
            + $"?select={select}"
            + $"&enabled=eq.true&limit={limit}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apikey", Config.AnonKey);
        request.Headers.Add("Authorization", $"Bearer {Config.AnonKey}");
        return request;
    }

    public void ConfigurePeriodicSync(Func<TimeSpan> getInterval, Func<Task> syncAction)
    {
        _periodicTimer?.Dispose();
        _periodicTimer = new Timer(_ =>
        {
            try
            {
                _ = syncAction();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Periodic sync error: {ex.Message}");
            }
        }, null, getInterval(), getInterval());
    }

    /// <summary>
    /// Performs a full sync: fetch enabled remote rows, download changed/missing
    /// images, verify SHA-256, upsert into the local SQLite library, then
    /// reconcile (deactivate local cloud mirrors no longer present remotely).
    /// Retries download failures with backoff. Falls back to local cache on any
    /// network failure.
    /// </summary>
    public async Task<SupabaseSyncResult> SyncAsync(ImageLibraryService library, string cacheDir, IProgress<int>? progress = null)
    {
        var result = new SupabaseSyncResult();
        if (SyncInProgress)
        {
            result.Error = "A sync is already in progress.";
            return result;
        }

        if (!Config.IsConfigured)
        {
            State = SupabaseState.NotConfigured;
            result.Error = "Supabase is not configured. Add a .env file (see .env.example).";
            SetLocalStateFallback();
            return result;
        }

        SyncInProgress = true;
        State = SupabaseState.Syncing;
        AppLog.Info($"Supabase: sync starting ({DiagnosticsSummary})");
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var rows = await FetchCatalogAsync(1000, _cts.Token).ConfigureAwait(false);
            result.RemoteRows = rows.Count;
            LastRemoteRows = rows.Count;
            AppLog.Info($"Supabase: catalog received, {rows.Count} enabled rows.");

            var syncDir = Directory.Exists(cacheDir)
                ? Path.Combine(cacheDir, "images")
                : cacheDir;
            Directory.CreateDirectory(syncDir);

            for (int i = 0; i < rows.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    await ProcessRowAsync(rows[i], syncDir, library, result, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    AppLog.Warn($"Supabase: failed to process {rows[i].Id}: {ex.Message}");
                }
                progress?.Report(i + 1);
            }

            // Reconciliation: any locally-present cloud mirror whose remote id is not
            // in the fetched catalog must be deactivated so it can never be selected.
            result.Reconciled += ReconcileStaleMirrors(library, rows.Select(r => r.Id).ToHashSet());

            LastSyncUtc = DateTime.UtcNow;
            LastSuccessUtc = DateTime.UtcNow;
            SyncFailuresInRow = 0;
            LastError = string.Empty;
            State = SupabaseState.Connected;
            result.Success = true;
            AppLog.Info($"Supabase: sync complete (downloaded={result.Downloaded}, imported={result.Imported}, skipped={result.Skipped}, failed={result.Failed}, reconciled={result.Reconciled}).");
            return result;
        }
        catch (OperationCanceledException)
        {
            State = SupabaseState.Offline;
            LastError = "Sync cancelled.";
            SyncFailuresInRow++;
            result.Error = LastError;
            SetLocalStateFallback();
            return result;
        }
        catch (Exception ex)
        {
            State = SupabaseState.Offline;
            LastError = ex.Message;
            SyncFailuresInRow++;
            result.Error = LastError;
            AppLog.Warn($"Supabase: sync error: {ex.Message}");
            SetLocalStateFallback();
            return result;
        }
        finally
        {
            SyncInProgress = false;
            LastReconciled = result.Reconciled;
        }
    }

    /// <summary>Deactivates local cloud mirrors whose remote row no longer exists or is disabled/deleted.</summary>
    private int ReconcileStaleMirrors(ImageLibraryService library, HashSet<string> liveRemoteIds)
    {
        var deactivated = 0;
        var all = library.GetAllImages();
        foreach (var img in all)
        {
            if (string.IsNullOrEmpty(img.RemoteId))
            {
                continue; // not a cloud mirror
            }
            if (img.Health == AssetHealth.Deleted || !string.IsNullOrEmpty(img.DeletedAt))
            {
                continue; // already gone
            }
            if (!liveRemoteIds.Contains(img.RemoteId))
            {
                img.Enabled = false;
                img.Health = AssetHealth.Deleted;
                img.DeletedAt = DateTime.UtcNow.ToString("o");
                library.UpdateImage(img);
                deactivated++;
                AppLog.Info($"Supabase: reconciled removed row {img.RemoteId} ({img.DisplayName}).");
            }
        }
        return deactivated;
    }

    private void SetLocalStateFallback()
    {
        LastSyncUtc ??= DateTime.UtcNow;
    }

    private async Task<List<RemoteImage>> FetchCatalogAsync(int limit, CancellationToken ct)
    {
        List<RemoteImage>? rows = null;
        Exception? lastError = null;

        for (int attempt = 0; attempt < CatalogSelectVariants.Length; attempt++)
        {
            using var request = BuildCatalogRequest(limit, CatalogSelectVariants[attempt]);
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (attempt > 0)
                {
                    AppLog.Warn($"Supabase: catalog OK on reduced query (variant {attempt + 1}/{CatalogSelectVariants.Length}).");
                }
                rows = ParseCatalog(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                break;
            }

            lastError = new InvalidOperationException(DescribeHttpError((int)response.StatusCode));
            if ((int)response.StatusCode != 400)
            {
                break; // only the query shape is worth retrying; other failures are terminal
            }
            AppLog.Warn($"Supabase: catalog query variant {attempt + 1} rejected (HTTP 400); retrying with a reduced column set.");
        }

        if (rows is null)
        {
            throw lastError ?? new InvalidOperationException("Catalog request failed.");
        }
        return rows;
    }

    private static List<RemoteImage> ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new List<RemoteImage>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(new RemoteImage
            {
                Id = GetString(el, "id") ?? "",
                DisplayName = GetString(el, "display_name") ?? "",
                StoragePath = GetString(el, "storage_path") ?? "",
                Category = GetString(el, "category"),
                Tags = GetString(el, "tags"),
                Enabled = GetBool(el, "enabled", true),
                Favorite = GetBool(el, "favorite", false),
                Weight = ClampWeight(GetInt(el, "weight", 1)),
                Sha256 = NormalizeSha(GetString(el, "sha256")),
                MimeType = GetString(el, "mime_type"),
                FileSize = GetLong(el, "file_size"),
                Width = GetInt(el, "width", 0) > 0 ? GetInt(el, "width", 0) : null,
                Height = GetInt(el, "height", 0) > 0 ? GetInt(el, "height", 0) : null,
                UpdatedAt = GetString(el, "updated_at")
            });
        }
        return rows.Where(r => !string.IsNullOrEmpty(r.StoragePath)).ToList();
    }

    private static int ClampWeight(int w) => w < 1 ? 1 : w;

    private async Task ProcessRowAsync(RemoteImage row, string syncDir, ImageLibraryService library, SupabaseSyncResult result, CancellationToken ct)
    {
        var existing = library.GetByRemoteId(row.Id);

        // If unchanged, skip the download entirely (no repeated downloads).
        if (existing is not null
            && existing.RemoteUpdatedAt == row.UpdatedAt
            && CelebrationImage.ExistsSafe(existing.FilePath)
            && existing.Health != AssetHealth.MissingCache
            && (string.IsNullOrEmpty(row.Sha256) || string.Equals(existing.Sha256, row.Sha256, StringComparison.OrdinalIgnoreCase)))
        {
            // Ensure not accidentally deactivated.
            if (existing.Health == AssetHealth.Deleted) { }
            result.Skipped++;
            return;
        }

        var ext = Path.GetExtension(row.StoragePath).ToLowerInvariant();
        if (ext is ".jpeg" or ".jfif") ext = ".jpg";
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var cacheFile = Path.Combine(syncDir, row.Id + ext);

        // Download with retry/backoff.
        var tempFile = Path.Combine(AppPaths.TempDir, "sup_" + Guid.NewGuid().ToString("N") + ext);
        Directory.CreateDirectory(AppPaths.TempDir);
        try
        {
            await DownloadWithRetryAsync(row, tempFile, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Supabase: download failed for {row.Id}: {ex.Message}");
            SafeDelete(tempFile);
            throw;
        }

        // Validate SHA-256 if provided; redownload once on mismatch.
        if (!string.IsNullOrEmpty(row.Sha256) && !string.Equals(ImageUtilities.ComputeSha256(tempFile), row.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Warn($"Supabase: SHA mismatch for {row.Id}, retrying download once.");
            try
            {
                File.Delete(tempFile);
                await DownloadWithRetryAsync(row, tempFile, ct).ConfigureAwait(false);
                if (!string.Equals(ImageUtilities.ComputeSha256(tempFile), row.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("SHA-256 mismatch after redownload.");
                }
            }
            catch (Exception ex)
            {
                SafeDelete(tempFile);
                AppLog.Warn($"Supabase: SHA validation failed for {row.Id}: {ex.Message}");
                throw;
            }
        }

        File.Copy(tempFile, cacheFile, overwrite: true);
        SafeDelete(tempFile);

        var now = DateTime.UtcNow.ToString("o");
        var mirror = new CelebrationImage
        {
            DisplayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.Id : row.DisplayName,
            FileName = Path.GetFileName(cacheFile),
            FilePath = cacheFile,
            Category = string.IsNullOrWhiteSpace(row.Category) ? "Cloud" : row.Category,
            Tags = row.Tags,
            Enabled = row.Enabled,
            Favorite = row.Favorite,
            Weight = row.Weight,
            Sha256 = row.Sha256,
            RemoteId = row.Id,
            StoragePath = row.StoragePath,
            RemoteUpdatedAt = row.UpdatedAt,
            LastSyncedAt = now,
            MimeType = row.MimeType,
            FileSize = row.FileSize,
            Width = row.Width,
            Height = row.Height,
            Health = AssetHealth.Ready,
            IsLocalOnly = false
        };
        library.UpsertRemoteImage(mirror);
        library.GenerateThumbnail(cacheFile, mirror.Id);

        result.Downloaded++;
        result.Imported++;
    }

    private async Task DownloadWithRetryAsync(RemoteImage row, string tempFile, CancellationToken ct)
    {
        const int maxAttempts = 3;
        int delayMs = 500;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await DownloadToFileAsync(row, tempFile, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    throw;
                }
                AppLog.Warn($"Supabase: download attempt {attempt} failed for {row.Id} ({ex.Message}); retrying in {delayMs}ms.");
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs *= 2;
            }
        }
    }

    private async Task DownloadToFileAsync(RemoteImage row, string tempFile, CancellationToken ct)
    {
        var url = $"{Config.Url}/storage/v1/object/{Config.Bucket}/{Uri.EscapeDataString(row.StoragePath ?? "")}?download=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apikey", Config.AnonKey);
        request.Headers.Add("Authorization", $"Bearer {Config.AnonKey}");
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Storage request failed ({(int)response.StatusCode}) for {row.Id}.");
        }
        await using (var fs = File.Create(tempFile))
        {
            await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
    }

    public async Task<SupabaseSyncResult> UploadFileAsync(string localFilePath, string displayName = null, string category = "Cloud", string tags = null)
    {
        var result = new SupabaseSyncResult();
        if (!Config.IsConfigured)
        {
            State = SupabaseState.NotConfigured;
            result.Error = "Supabase is not configured. Add a .env file (see .env.example).";
            return result;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            result.Error = $"File not found: {localFilePath}";
            return result;
        }

        var fileBytes = await File.ReadAllBytesAsync(localFilePath);
        var displayNameFallback = Path.GetFileNameWithoutExtension(localFilePath);
        var ext = Path.GetExtension(localFilePath).ToLowerInvariant();

        // Generate canonical UUID-based storage key. NEVER uses the user's filename.
        var storagePath = StorageObjectKey.Create(StorageObjectKey.MediaType.Images, ext);
        if (storagePath.EndsWith(".bin"))
        {
            result.Error = $"Unsupported image type: {ext}. Supported: .png, .jpg, .jpeg, .webp, .gif";
            AppLog.Warn($"Supabase: rejected unsupported image extension '{ext}' for {Path.GetFileName(localFilePath)}.");
            return result;
        }

        // Compute SHA-256 before upload
        var sha256 = ImageUtilities.ComputeSha256(localFilePath);

        // Upload to Supabase storage bucket via the REST object API.
        // Endpoint: POST /storage/v1/object/{bucket}/{path}
        // The object path is a canonical UUID-based key — user filenames are never used in the path.
        var uploadUrl = $"{Config.Url}/storage/v1/object/{Config.Bucket}/{storagePath}";
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("apikey", Config.AnonKey);
        uploadRequest.Headers.Add("Authorization", $"Bearer {Config.AnonKey}");
        var mimeType = StorageObjectKey.GetMimeType(StorageObjectKey.MediaType.Images, ext);
        uploadRequest.Content = new ByteArrayContent(fileBytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = fileBytes.Length;

        try
        {
            var uploadResponse = await Http.SendAsync(uploadRequest).ConfigureAwait(false);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorMsg = await uploadResponse.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                AppLog.Warn($"Supabase: upload failed ({(int)uploadResponse.StatusCode}): {errorMsg}");
                result.Error = $"Upload failed: {DescribeUploadError((int)uploadResponse.StatusCode, storagePath)}";
                return result;
            }

            // Insert row into celebration_images so the catalog sync picks it up
            var storageUri = $"{Config.Url}/storage/v1/object/{Config.Bucket}/{storagePath}";
            var now = DateTime.UtcNow.ToString("o");
            var image = new CelebrationImage
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = displayName ?? displayNameFallback,
                FileName = Path.GetFileName(localFilePath),
                FilePath = localFilePath,
                Category = category,
                Tags = tags,
                Enabled = true,
                Favorite = false,
                Weight = 1,
                Sha256 = sha256,
                RemoteId = Guid.NewGuid().ToString(),
                StoragePath = storagePath,
                RemoteUpdatedAt = now,
                LastSyncedAt = now,
                MimeType = $"image/{ext.TrimStart('.')}",
                FileSize = fileBytes.Length,
                Width = 0,
                Height = 0,
                Health = AssetHealth.Ready,
                IsLocalOnly = false
            };

            // Insert into local SQLite catalog
            try
            {
                var db = new DatabaseService(AppPaths.DatabasePath);
                db.Initialize();
                db.InsertImage(image);
                AppLog.Info($"Supabase: inserted image {image.Id} ({image.DisplayName}) into celebration_images.");
            }
            catch (Exception dbEx)
            {
                AppLog.Warn($"Supabase: failed to insert into celebration_images: {dbEx.Message}");
                // Not critical - the image is still in storage, just won't appear in catalog until manually added
            }

            result.Success = true;
            result.Downloaded = 1; // uploading counts as "synced"
            AppLog.Info($"Supabase: uploaded {localFilePath} to bucket '{Config.Bucket}' as {storagePath}");
        }
        catch (Exception ex)
        {
            result.Error = $"Upload error: {ex.Message}";
            AppLog.Warn($"Supabase: upload error: {ex.Message}");
        }

        return result;
    }

    public async Task<SupabaseSyncResult> UploadAudioAsync(string localFilePath, string displayName = null)
    {
        var result = new SupabaseSyncResult();
        if (!Config.IsConfigured)
        {
            State = SupabaseState.NotConfigured;
            result.Error = "Supabase is not configured. Add a .env file (see .env.example).";
            return result;
        }

        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            result.Error = $"File not found: {localFilePath}";
            return result;
        }

        var fileBytes = await File.ReadAllBytesAsync(localFilePath);
        var displayNameFallback = Path.GetFileNameWithoutExtension(localFilePath);
        var ext = Path.GetExtension(localFilePath).ToLowerInvariant();

        // Generate canonical UUID-based storage key. NEVER uses the user's filename.
        var storagePath = StorageObjectKey.Create(StorageObjectKey.MediaType.Audio, ext);
        if (storagePath.EndsWith(".bin"))
        {
            result.Error = $"Unsupported audio type: {ext}. Supported: .wav, .mp3, .m4a, .ogg, .wma";
            AppLog.Warn($"Supabase: rejected unsupported audio extension '{ext}' for {Path.GetFileName(localFilePath)}.");
            return result;
        }

        // Compute SHA-256 before upload
        var sha256 = ImageUtilities.ComputeSha256(localFilePath);

        // Upload to Supabase storage bucket via the REST object API
        // Endpoint: POST /storage/v1/object/{bucket}/{path}  (the legacy "?upload=true&path=" route is gone)
        // The object path is a canonical UUID-based key.
        var uploadUrl = $"{Config.Url}/storage/v1/object/{Config.Bucket}/{storagePath}";
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.Add("apikey", Config.AnonKey);
        uploadRequest.Headers.Add("Authorization", $"Bearer {Config.AnonKey}");
        var mimeType = StorageObjectKey.GetMimeType(StorageObjectKey.MediaType.Audio, ext);
        uploadRequest.Content = new ByteArrayContent(fileBytes);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = fileBytes.Length;

        try
        {
            var uploadResponse = await Http.SendAsync(uploadRequest).ConfigureAwait(false);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorMsg = await uploadResponse.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                AppLog.Warn($"Supabase: audio upload failed ({(int)uploadResponse.StatusCode}): {errorMsg}");
                result.Error = $"Audio upload failed: {DescribeUploadError((int)uploadResponse.StatusCode, storagePath)}";
                return result;
            }

            // Insert row into celebration_images with category="Audio"
            var storageUri = $"{Config.Url}/storage/v1/object/{Config.Bucket}/{storagePath}";
            var now = DateTime.UtcNow.ToString("o");
            var audioId = Guid.NewGuid().ToString();

            var image = new CelebrationImage
            {
                Id = audioId,
                DisplayName = displayName ?? displayNameFallback,
                FileName = Path.GetFileName(localFilePath),
                FilePath = localFilePath,
                Category = "Audio",
                Tags = null,
                Enabled = true,
                Favorite = false,
                Weight = 1,
                Sha256 = sha256,
                RemoteId = audioId,
                StoragePath = storagePath,
                RemoteUpdatedAt = now,
                LastSyncedAt = now,
                MimeType = mimeType,
                FileSize = fileBytes.Length,
                Width = 0,
                Height = 0,
                Health = AssetHealth.Ready,
                IsLocalOnly = false
            };

            // Insert into local SQLite catalog
            try
            {
                var db = new DatabaseService(AppPaths.DatabasePath);
                db.Initialize();
                db.InsertImage(image);
                AppLog.Info($"Supabase: inserted audio {audioId} ({image.DisplayName}) into celebration_images.");
            }
            catch (Exception dbEx)
            {
                AppLog.Warn($"Supabase: failed to insert audio into celebration_images: {dbEx.Message}");
            }

            result.Success = true;
            AppLog.Info($"Supabase: uploaded audio {localFilePath} to bucket '{Config.Bucket}' as {storagePath}");
        }
        catch (Exception ex)
        {
            result.Error = $"Audio upload error: {ex.Message}";
            AppLog.Warn($"Supabase: audio upload error: {ex.Message}");
        }

        return result;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public void CancelSync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }
    }

    public void Dispose()
    {
        CancelSync();
        _cts?.Dispose();
        _periodicTimer?.Dispose();
    }

    // ---- DTO helpers ----

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }
        return null;
    }

    private static bool GetBool(JsonElement el, string name, bool def)
    {
        if (el.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
            if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b)) return b;
        }
        return def;
    }

    private static int GetInt(JsonElement el, string name, int def)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
        {
            return v;
        }
        return def;
    }

    private static long? GetLong(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)) return v;
            if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var l)) return l;
        }
        return null;
    }

    /// <summary>Normalizes a Postgres bytea hex ("\\x...") or plain hex/digest.</summary>
    private static string? NormalizeSha(string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha)) return null;
        sha = sha.Trim().ToLowerInvariant();
        if (sha.StartsWith("\\x")) sha = sha[2..];
        return sha;
    }

    private class RemoteImage
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string StoragePath { get; set; } = "";
        public string? Category { get; set; }
        public string? Tags { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Favorite { get; set; }
        public int Weight { get; set; } = 1;
        public string? Sha256 { get; set; }
        public string? MimeType { get; set; }
        public long? FileSize { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? UpdatedAt { get; set; }
    }
}