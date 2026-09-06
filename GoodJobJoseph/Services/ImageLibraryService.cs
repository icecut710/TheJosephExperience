using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

public class ImageLibraryService
{
    private readonly DatabaseService _db;
    private readonly string _imagesDir;
    private readonly string _thumbnailsDir;
    private readonly object _shuffleLock = new();
    private Queue<string>? _shuffleBag;
    private HashSet<string> _brokenIds = new();

    public ImageLibraryService(DatabaseService db, string? imagesDir = null, string? thumbnailsDir = null)
    {
        _db = db;
        _imagesDir = imagesDir ?? AppPaths.ImagesDir;
        _thumbnailsDir = thumbnailsDir ?? AppPaths.ThumbnailsDir;
        try { Directory.CreateDirectory(_imagesDir); } catch { }
        try { Directory.CreateDirectory(_thumbnailsDir); } catch { }
    }

    public List<CelebrationImage> GetAllImages() => _db.GetAllImages();

    public CelebrationImage? GetImage(string id) => _db.GetImage(id);

    public CelebrationImage? GetByRemoteId(string remoteId) => _db.GetImageByRemoteId(remoteId);

    public CelebrationImage UpsertRemoteImage(CelebrationImage mirror) => _db.UpsertRemoteImage(mirror);

    public void UpdateImage(CelebrationImage image) => _db.UpdateImage(image);

    public void MarkDeleted(string id)
    {
        var img = _db.GetImage(id);
        if (img is null) return;
        img.Health = AssetHealth.Deleted;
        img.DeletedAt = DateTime.UtcNow.ToString("o");
        img.Enabled = false;
        _db.UpdateImage(img);
    }

    public LibraryStats GetStats() => _db.GetStats();

    public List<CelebrationHistory> GetRecentHistory(int limit = 50) => _db.GetRecentHistory(limit);

    /// <summary>
    /// Returns celebration history bucketed by time range for graph rendering.
    /// Uses real SQLite history data - no fake values. Bucket sizes are
    /// chosen per range so charts stay readable and cheap on low-end machines.
    /// </summary>
    public List<TimeBucket> GetHistoryBuckets(HistoryRange range)
    {
        var now = DateTime.UtcNow;
        var (since, bucket) = range switch
        {
            HistoryRange.Hours24 => (now.AddHours(-24), TimeSpan.FromHours(1)),
            HistoryRange.Days7 => (now.AddDays(-7), TimeSpan.FromHours(6)),
            HistoryRange.Days30 => (now.AddDays(-30), TimeSpan.FromDays(1)),
            HistoryRange.Days90 => (now.AddDays(-90), TimeSpan.FromDays(1)),
            HistoryRange.All => (now.AddDays(-730), TimeSpan.FromDays(1)),
            _ => (now.AddHours(-24), TimeSpan.FromHours(1))
        };

        return _db.GetHistoryBuckets(since, bucket);
    }

    // ===============================================================
    // DELETE SYSTEM — single authoritative command
    // ===============================================================
    public enum DeleteScope
    {
        LocalOnly,
        RemoveCache
    }

    public DeleteCommandResult DeleteImageCommand(CelebrationImage image, DeleteScope scope)
    {
        var result = new DeleteCommandResult();
        if (image is null)
        {
            result.Error = "Image not found.";
            return result;
        }

        _brokenIds.Remove(image.Id);

        switch (scope)
        {
            case DeleteScope.RemoveCache:
                // Remove only the local file + thumbnail; keep the DB row so next sync can redownload.
                result.RemovedCache = TryDeleteFiles(image, deleteFile: true, deleteThumb: true, keepRow: true);
                image.Health = AssetHealth.MissingCache;
                image.Enabled = false;
                image.DeletedAt = null;
                _db.UpdateImage(image);
                result.RemovedFromSelection = true;
                break;

            case DeleteScope.LocalOnly:
            default:
                // Soft-delete the local row + remove files + remove from selection permanently.
                _db.UpdateImage(MarkLocalDeleted(image));
                result.RemovedFiles = TryDeleteFiles(image, deleteFile: true, deleteThumb: true, keepRow: false);
                result.RemovedFromSelection = true;
                result.LocalRowDeleted = true;
                break;
        }

        ClearCacheFor(image.Id);
        return result;
    }

    private static CelebrationImage MarkLocalDeleted(CelebrationImage image)
    {
        image.Enabled = false;
        image.Health = AssetHealth.Deleted;
        image.DeletedAt = DateTime.UtcNow.ToString("o");
        return image;
    }

    private bool TryDeleteFiles(CelebrationImage image, bool deleteFile, bool deleteThumb, bool keepRow)
    {
        var ok = true;
        if (deleteFile && CelebrationImage.ExistsSafe(image.FilePath))
        {
            try
            {
                File.Delete(image.FilePath);
            }
            catch
            {
                ok = false;
            }
        }
        if (deleteThumb && !string.IsNullOrWhiteSpace(image.Id))
        {
            var thumb = Path.Combine(AppPaths.ThumbnailsDir, $"{image.Id}.png");
            if (File.Exists(thumb))
            {
                try { File.Delete(thumb); } catch { ok = false; }
            }
        }
        if (!keepRow)
        {
            _db.DeleteImage(image.Id);
        }
        return ok;
    }

    /// <summary>Clears in-memory / WPF bitmap caches so Windows can release file handles.</summary>
    public static void ClearCacheFor(string imageId)
    {
        // Bitmaps were loaded with BitmapCacheOption.OnLoad, so the stream is already closed.
        // Force a GC pass to release any lingering decoder references on managed FX.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // ===============================================================
    // IMPORT
    // ===============================================================

    public ImportResult ImportFile(string sourcePath, string? displayName = null, string? category = null, string? tags = null)
    {
        var result = new ImportResult();
        try
        {
            if (!File.Exists(sourcePath))
            {
                result.Failed++;
                result.LastError = "File not found.";
                return result;
            }
            if (!ImageUtilities.IsSupportedImageFile(sourcePath))
            {
                result.Failed++;
                result.LastError = "Unsupported image format.";
                return result;
            }

            var sha = ImageUtilities.ComputeSha256(sourcePath);
            var existing = _db.GetImageBySha256(sha);
            if (existing is not null)
            {
                result.Skipped++;
                result.SkippedDuplicates++;
                return result;
            }

            var id = Guid.NewGuid().ToString();
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".jfif" || ext == ".jpeg") ext = ".jpg";
            if (!ImageUtilities.CanCopyExtension(sourcePath))
            {
                return ImportWithReencode(sourcePath, id, displayName, category, tags, sha, result);
            }

            var fileName = $"{id}{ext}";
            var destPath = Path.Combine(_imagesDir, fileName);
            File.Copy(sourcePath, destPath, overwrite: false);

            var image = BuildImportedImage(id, displayName, sourcePath, fileName, destPath, category, tags, sha);
            _db.InsertImage(image);
            GenerateThumbnail(destPath, id);
            result.Imported++;
            result.ImportedImage = _db.GetImage(id);
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.LastError = ex.Message;
        }
        return result;
    }

    private static CelebrationImage BuildImportedImage(string id, string? displayName, string sourcePath, string fileName, string destPath, string? category, string? tags, string sha)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? LorePools.RandomName()
            : displayName.Trim();
        var quote = LorePools.RandomQuote();

        return new CelebrationImage
        {
            Id = id,
            DisplayName = name,
            FileName = fileName,
            FilePath = destPath,
            Category = category,
            Tags = tags,
            Enabled = true,
            Favorite = false,
            Weight = 1,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o"),
            Sha256 = sha,
            IsLocalOnly = true,
            Health = GetImageHealth(destPath),
            CelebrationText = quote
        };
    }

    private static AssetHealth GetImageHealth(string path)
    {
        return CelebrationImage.ExistsSafe(path) ? AssetHealth.Ready : AssetHealth.MissingCache;
    }

    private ImportResult ImportWithReencode(string sourcePath, string id, string? displayName, string? category, string? tags, string sha, ImportResult result)
    {
        try
        {
            string destPath = Path.Combine(_imagesDir, $"{id}.png");
            var encoder = new PngBitmapEncoder();
            using (var stream = File.OpenRead(sourcePath))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames == null || decoder.Frames.Count == 0)
                {
                    result.Failed++;
                    return result;
                }
                encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                using var outStream = File.Create(destPath);
                encoder.Save(outStream);
            }

            var image = BuildImportedImage(id, displayName, sourcePath, Path.GetFileName(destPath), destPath, category, tags, sha);
            _db.InsertImage(image);
            GenerateThumbnail(destPath, id);
            result.Imported++;
            result.ImportedImage = _db.GetImage(id);
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.LastError = ex.Message;
        }
        return result;
    }

    public async Task<ImportResult> ImportManyAsync(IEnumerable<string> paths, IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path) && ImageUtilities.IsSupportedImageFile(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (ImageUtilities.IsSupportedImageFile(f))
                    {
                        files.Add(f);
                    }
                }
            }
        }

        var processed = 0;
        foreach (var file in files)
        {
            var single = await Task.Run(() => ImportFile(file));
            result.Imported += single.Imported;
            result.Skipped += single.Skipped;
            result.SkippedDuplicates += single.SkippedDuplicates;
            result.Failed += single.Failed;
            if (single.Imported > 0 && single.ImportedImage is not null)
            {
                result.ImportedImages.Add(single.ImportedImage);
            }
            processed++;
            progress?.Report(new ImportProgress { Processed = processed, Total = files.Count });
        }
        return result;
    }

    // ===============================================================
    // RANDOM SELECTION — all modes
    // ===============================================================

    public CelebrationImage? PickImage(ImageMode mode, bool favoritesOnly, bool avoidRepeats, Guid? specificId, string? lastShownId, int repeatCooldown = 1, string? category = null)
    {
        var images = _db.GetEligibleImages(false);
        if (images.Count == 0)
        {
            return null;
        }

        // Only Ready assets whose file exists.
        var ready = images.Where(i => i.IsReady && !_brokenIds.Contains(i.Id) && CelebrationImage.ExistsSafe(i.FilePath)).ToList();
        if (ready.Count == 0)
        {
            ready = images.Where(i => i.Enabled && !_brokenIds.Contains(i.Id) && CelebrationImage.ExistsSafe(i.FilePath)).ToList();
            if (ready.Count == 0)
            {
                return null;
            }
        }

        if (favoritesOnly)
        {
            var favs = ready.Where(i => i.Favorite).ToList();
            if (favs.Count > 0) ready = favs;
        }

        switch (mode)
        {
            case ImageMode.Specific:
                if (specificId.HasValue)
                {
                    var specific = ready.FirstOrDefault(i => i.Id == specificId.Value.ToString());
                    if (specific is not null) return specific;
                }
                break;

            case ImageMode.Weighted:
                return PickWeighted(ready, avoidRepeats, lastShownId);

            case ImageMode.LeastRecentlyUsed:
                return PickLeastRecentlyUsed(ready, lastShownId, avoidRepeats);

            case ImageMode.ShuffleBag:
                return PickShuffleBag(ready);

            case ImageMode.RecentlyAdded:
                return PickRecentlyAdded(ready, avoidRepeats, lastShownId);

            case ImageMode.RandomCategory:
                if (!string.IsNullOrWhiteSpace(category))
                {
                    var inCat = ready.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (inCat.Count > 0)
                    {
                        ready = inCat;
                    }
                }
                break;

            case ImageMode.Random:
            case ImageMode.FavoritesOnly:
            default:
                break;
        }

        if (avoidRepeats && ready.Count > repeatCooldown && lastShownId is not null)
        {
            var others = ready.Where(i => i.Id != lastShownId).ToList();
            if (others.Count > 0) ready = others;
        }

        return ready[Random.Shared.Next(ready.Count)];
    }

    private CelebrationImage? PickWeighted(List<CelebrationImage> candidates, bool avoidRepeats, string? lastShownId)
    {
        var pool = candidates.Where(i => i.Weight > 0).ToList();
        if (pool.Count == 0) return candidates.Count > 0 ? candidates[0] : null;
        if (avoidRepeats && pool.Count > 1 && lastShownId is not null)
        {
            var others = pool.Where(i => i.Id != lastShownId).ToList();
            if (others.Count > 0) pool = others;
        }
        var totalWeight = pool.Sum(i => i.Weight);
        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        foreach (var image in pool)
        {
            cumulative += image.Weight;
            if (roll < cumulative) return image;
        }
        return pool[^1];
    }

    private static CelebrationImage? PickLeastRecentlyUsed(List<CelebrationImage> candidates, string? lastShownId, bool avoidRepeats)
    {
        if (avoidRepeats && candidates.Count > 1 && lastShownId is not null)
        {
            var others = candidates.Where(i => i.Id != lastShownId).ToList();
            if (others.Count > 0)
            {
                foreach (var c in others) c.LastShownAt ??= DateTime.MinValue.ToString("o");
                return others.OrderBy(i => i.LastShownAt).First();
            }
        }
        foreach (var c in candidates) c.LastShownAt ??= DateTime.MinValue.ToString("o");
        return candidates.OrderBy(i => i.LastShownAt).First();
    }

    private CelebrationImage? PickShuffleBag(List<CelebrationImage> candidates)
    {
        var ordered = candidates.OrderBy(i => i.Id).ToList();
        lock (_shuffleLock)
        {
            if (_shuffleBag == null || _shuffleBag.Count == 0)
            {
                _shuffleBag = new Queue<string>(ordered.OrderBy(_ => Random.Shared.Next()).Select(i => i.Id));
            }
            while (_shuffleBag.Count > 0)
            {
                var id = _shuffleBag.Dequeue();
                var match = ordered.FirstOrDefault(i => i.Id == id);
                if (match is not null) return match;
            }
        }
        return ordered.FirstOrDefault();
    }

    private static CelebrationImage? PickRecentlyAdded(List<CelebrationImage> candidates, bool avoidRepeats, string? lastShownId)
    {
        var pool = candidates.OrderByDescending(i => i.CreatedAt).ToList();
        if (avoidRepeats && pool.Count > 1 && lastShownId is not null)
        {
            var others = pool.Where(i => i.Id != lastShownId).ToList();
            if (others.Count > 0) pool = others;
        }
        return pool.FirstOrDefault();
    }

    public void RecordShown(string imageId, string triggerType, int durationMs)
    {
        _db.IncrementTimesShown(imageId);
        _db.InsertHistory(new CelebrationHistory
        {
            ImageId = imageId,
            TriggerType = triggerType,
            ShownAt = DateTime.UtcNow.ToString("o"),
            DurationMs = durationMs
        });
    }

    /// <summary>Marks a broken/decode-failing asset so it's excluded from Ready pool until repaired.</summary>
    public void MarkBroken(string imageId)
    {
        _brokenIds.Add(imageId);
        var img = _db.GetImage(imageId);
        if (img is not null && img.Health == AssetHealth.Ready)
        {
            img.Health = AssetHealth.Broken;
            _db.UpdateImage(img);
        }
    }

    public void RepairImage(string imageId)
    {
        _brokenIds.Remove(imageId);
        var img = _db.GetImage(imageId);
        if (img is not null)
        {
            var decodes = LoadImageSource(img.FilePath) is not null;
            img.Health = decodes && CelebrationImage.ExistsSafe(img.FilePath) ? AssetHealth.Ready : AssetHealth.MissingCache;
            _db.UpdateImage(img);
        }
    }

    // ===============================================================
    // INTEGRITY SCAN
    // ===============================================================

    public IntegrityResult RunIntegrityScan()
    {
        var result = new IntegrityResult();
        var images = _db.GetAllImages();
        foreach (var img in images)
        {
            if (img.Health == AssetHealth.Deleted || !string.IsNullOrEmpty(img.DeletedAt))
            {
                result.Stale++; continue;
            }
            if (!CelebrationImage.ExistsSafe(img.FilePath))
            {
                if (img.Health != AssetHealth.MissingCache)
                {
                    img.Health = AssetHealth.MissingCache;
                    _db.UpdateImage(img);
                }
                result.MissingCache++;
            }
            else if (img.Health == AssetHealth.Ready || img.Health == AssetHealth.MissingCache)
            {
                var ok = LoadImageSourceSafe(img.FilePath);
                if (ok)
                {
                    if (img.Health != AssetHealth.Ready)
                    {
                        img.Health = AssetHealth.Ready;
                        _db.UpdateImage(img);
                    }
                    result.Ready++;
                }
                else
                {
                    if (img.Health != AssetHealth.Broken)
                    {
                        img.Health = AssetHealth.Broken;
                        _db.UpdateImage(img);
                    }
                    result.Broken++;
                }
            }
            else if (img.Health == AssetHealth.Broken)
            {
                result.Broken++;
            }
            else if (img.Health == AssetHealth.Downloading)
            {
                result.MissingCache++;
            }
            if (string.IsNullOrEmpty(img.RemoteId) && img.IsLocalOnly)
            {
                result.LocalOnly++;
            }
        }
        result.Total = images.Count;
        return result;
    }

    public static bool LoadImageSourceSafe(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames != null && decoder.Frames.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    // ===============================================================
    // THUMBNAILS
    // ===============================================================

    public string GetThumbnailPath(string imageId) => Path.Combine(_thumbnailsDir, $"{imageId}.png");

    public BitmapSource? LoadThumbnail(string imageId, string filePath)
    {
        var thumbPath = GetThumbnailPath(imageId);
        if (File.Exists(thumbPath))
        {
            try { return LoadBitmap(thumbPath); }
            catch { }
        }
        if (File.Exists(filePath))
        {
            try
            {
                GenerateThumbnail(filePath, imageId);
                return LoadBitmap(thumbPath);
            }
            catch { return null; }
        }
        return null;
    }

    public static object? LoadImageSource(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                // Return a BitmapImage with GIF animation frames.
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp.EndInit();
                return bmp;
            }
            return LoadBitmap(filePath);
        }
        catch { return null; }
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    public void GenerateThumbnail(string filePath, string imageId)
    {
        try
        {
            var thumbPath = GetThumbnailPath(imageId);
            if (File.Exists(thumbPath)) return;
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var scale = 256.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
            var width = (int)Math.Max(1, frame.PixelWidth * scale);
            var height = (int)Math.Max(1, frame.PixelHeight * scale);
            var resized = new TransformedBitmap(frame, new ScaleTransform(width / (double)frame.PixelWidth, height / (double)frame.PixelHeight));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            using var outStream = File.Create(thumbPath);
            encoder.Save(outStream);
        }
        catch
        {
        }
    }
}

public class DeleteCommandResult
{
    public bool RemovedFiles { get; set; }
    public bool RemovedCache { get; set; }
    public bool RemovedFromSelection { get; set; }
    public bool LocalRowDeleted { get; set; }
    public string? Error { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class IntegrityResult
{
    public int Total { get; set; }
    public int Ready { get; set; }
    public int MissingCache { get; set; }
    public int Broken { get; set; }
    public int Stale { get; set; }
    public int LocalOnly { get; set; }
}

public class ImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int SkippedDuplicates { get; set; }
    public int Failed { get; set; }
    public string? LastError { get; set; }
    public CelebrationImage? ImportedImage { get; set; }
    public List<CelebrationImage> ImportedImages { get; set; } = new();
}

public class ImportProgress
{
    public int Processed { get; set; }
    public int Total { get; set; }
}