using System.Data;
using Microsoft.Data.Sqlite;
using JosephExperience.Models;

namespace JosephExperience.Data;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private readonly object _connectionLock = new();

    public DatabaseService(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ToString();
    }

    public SqliteConnection GetConnection()
    {
        lock (_connectionLock)
        {
            if (_connection is null || _connection.State != ConnectionState.Open)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
                DatabaseMigrations.Migrate(_connection);
            }
            return _connection;
        }
    }

    public void Initialize()
    {
        GetConnection();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        SqliteConnection.ClearAllPools();
    }

    // ---------------------------------------------------------------
    // Image CRUD
    // ---------------------------------------------------------------

    private const string ImageColumns = @"
            id, display_name, file_name, file_path, category, tags, enabled,
            favorite, weight, times_shown, created_at, updated_at, last_shown_at, sha256,
            remote_id, storage_path, remote_updated_at, last_synced_at,
            mime_type, file_size, width, height, health, is_local_only, deleted_at,
            celebration_text, sound_id";

    public List<CelebrationImage> GetAllImages()
    {
        var result = new List<CelebrationImage>();
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {ImageColumns} FROM celebration_images ORDER BY created_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadImage(reader));
        }
        return result;
    }

    public CelebrationImage? GetImage(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {ImageColumns} FROM celebration_images WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadImage(reader) : null;
    }

    public CelebrationImage? GetImageBySha256(string sha256)
    {
        if (string.IsNullOrEmpty(sha256))
        {
            return null;
        }
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {ImageColumns} FROM celebration_images WHERE sha256 = $sha256 LIMIT 1";
        cmd.Parameters.AddWithValue("$sha256", sha256);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadImage(reader) : null;
    }

    public CelebrationImage? GetImageByRemoteId(string remoteId)
    {
        if (string.IsNullOrEmpty(remoteId))
        {
            return null;
        }
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {ImageColumns} FROM celebration_images WHERE remote_id = $remote_id LIMIT 1";
        cmd.Parameters.AddWithValue("$remote_id", remoteId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadImage(reader) : null;
    }

    /// <summary>
    /// Inserts or updates a locally-synced mirror of a remote image, keyed by
    /// stable remote_id. Returns the resulting local row; never duplicates.
    /// </summary>
    public CelebrationImage UpsertRemoteImage(CelebrationImage remote)
    {
        var now = DateTime.UtcNow.ToString("o");
        var existing = !string.IsNullOrEmpty(remote.RemoteId)
            ? GetImageByRemoteId(remote.RemoteId)
            : (!string.IsNullOrEmpty(remote.Sha256) ? GetImageBySha256(remote.Sha256) : null);

        if (existing is not null)
        {
            existing.DisplayName = remote.DisplayName;
            existing.Category = remote.Category;
            existing.Tags = remote.Tags;
            existing.Enabled = remote.Enabled;
            existing.Favorite = remote.Favorite;
            existing.Weight = remote.Weight;
            existing.Sha256 = remote.Sha256;
            existing.RemoteId = remote.RemoteId;
            existing.StoragePath = remote.StoragePath;
            existing.RemoteUpdatedAt = remote.RemoteUpdatedAt;
            existing.LastSyncedAt = now;
            existing.MimeType = remote.MimeType;
            existing.FileSize = remote.FileSize;
            existing.Width = remote.Width;
            existing.Height = remote.Height;
            existing.IsLocalOnly = false;
            existing.DeletedAt = null;
            if (!string.IsNullOrEmpty(remote.FileName)) existing.FileName = remote.FileName;
            if (!string.IsNullOrEmpty(remote.FilePath)) existing.FilePath = remote.FilePath;
            if (!string.IsNullOrEmpty(remote.CelebrationText)) existing.CelebrationText = remote.CelebrationText;
            UpdateImage(existing);
            return existing;
        }

        var fresh = new CelebrationImage
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = remote.DisplayName,
            FileName = remote.FileName,
            FilePath = remote.FilePath,
            Category = remote.Category,
            Tags = remote.Tags,
            Enabled = remote.Enabled,
            Favorite = remote.Favorite,
            Weight = remote.Weight,
            Sha256 = remote.Sha256,
            RemoteId = remote.RemoteId,
            StoragePath = remote.StoragePath,
            RemoteUpdatedAt = remote.RemoteUpdatedAt,
            LastSyncedAt = now,
            MimeType = remote.MimeType,
            FileSize = remote.FileSize,
            Width = remote.Width,
            Height = remote.Height,
            IsLocalOnly = false,
            Health = AssetHealth.MissingCache,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o")
        };
        InsertImage(fresh);
        return fresh;
    }

    public void InsertImage(CelebrationImage image)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO celebration_images
                (id, display_name, file_name, file_path, category, tags, enabled, favorite,
                 weight, times_shown, created_at, updated_at, last_shown_at, sha256,
                 remote_id, storage_path, remote_updated_at, last_synced_at,
                 mime_type, file_size, width, height, health, is_local_only, deleted_at,
                 celebration_text, sound_id)
            VALUES
                ($id, $display_name, $file_name, $file_path, $category, $tags, $enabled, $favorite,
                 $weight, $times_shown, $created_at, $updated_at, $last_shown_at, $sha256,
                 $remote_id, $storage_path, $remote_updated_at, $last_synced_at,
                 $mime_type, $file_size, $width, $height, $health, $is_local_only, $deleted_at,
                 $celebration_text, $sound_id)";
        AddImageParameters(cmd, image);
        cmd.ExecuteNonQuery();
    }

    public void UpdateImage(CelebrationImage image)
    {
        image.UpdatedAt = DateTime.UtcNow.ToString("o");
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE celebration_images SET
                display_name = $display_name,
                category = $category,
                tags = $tags,
                enabled = $enabled,
                favorite = $favorite,
                weight = $weight,
                updated_at = $updated_at,
                remote_id = $remote_id,
                storage_path = $storage_path,
                remote_updated_at = $remote_updated_at,
                last_synced_at = $last_synced_at,
                mime_type = $mime_type,
                file_size = $file_size,
                width = $width,
                height = $height,
                health = $health,
                is_local_only = $is_local_only,
                deleted_at = $deleted_at,
                celebration_text = $celebration_text,
                sound_id = $sound_id
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", image.Id);
        cmd.Parameters.AddWithValue("$display_name", image.DisplayName);
        cmd.Parameters.AddWithValue("$category", (object?)image.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", (object?)image.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", image.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$favorite", image.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$weight", image.Weight);
        cmd.Parameters.AddWithValue("$updated_at", image.UpdatedAt);
        cmd.Parameters.AddWithValue("$remote_id", (object?)image.RemoteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$storage_path", (object?)image.StoragePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$remote_updated_at", (object?)image.RemoteUpdatedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_synced_at", (object?)image.LastSyncedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mime_type", (object?)image.MimeType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$file_size", (object?)image.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$width", (object?)image.Width ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$height", (object?)image.Height ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$health", image.Health.ToString());
        cmd.Parameters.AddWithValue("$is_local_only", image.IsLocalOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$deleted_at", (object?)image.DeletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$celebration_text", (object?)image.CelebrationText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sound_id", (object?)image.SoundId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

        public void DeleteImage(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        // Delete dependent history first to avoid FOREIGN KEY constraint
        // failures (celebration_history references celebration_images).
        cmd.CommandText = @"
            DELETE FROM celebration_history WHERE image_id = $id;
            DELETE FROM celebration_images WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void IncrementTimesShown(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE celebration_images
            SET times_shown = times_shown + 1,
                last_shown_at = $now,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------
    // History
    // ---------------------------------------------------------------

    public void InsertHistory(CelebrationHistory history)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO celebration_history (image_id, trigger_type, shown_at, duration_ms)
            VALUES ($image_id, $trigger_type, $shown_at, $duration_ms)";
        cmd.Parameters.AddWithValue("$image_id", (object?)history.ImageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$trigger_type", (object?)history.TriggerType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$shown_at", history.ShownAt);
        cmd.Parameters.AddWithValue("$duration_ms", history.DurationMs);
        cmd.ExecuteNonQuery();
    }

    public LibraryStats GetStats()
    {
        var stats = new LibraryStats();
        using var connection = GetConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM celebration_history";
            stats.TotalCelebrations = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM celebration_images";
            stats.ImageCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM celebration_images WHERE enabled = 1";
            stats.EnabledImageCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT ci.display_name FROM celebration_history h
                INNER JOIN celebration_images ci ON ci.id = h.image_id
                GROUP BY h.image_id ORDER BY COUNT(h.image_id) DESC LIMIT 1";
            stats.MostCelebratedJoseph = cmd.ExecuteScalar() as string;
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT MAX(shown_at) FROM celebration_history";
            var max = cmd.ExecuteScalar();
            if (max is not null && max is not DBNull)
            {
                stats.LastCelebration = DateTime.TryParse(max.ToString(), out var dt) ? dt.ToLocalTime() : null;
            }
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM app_metadata WHERE key = 'joseph_coins'";
            stats.JosephCoins = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(SUM(times_played), 0) FROM celebration_sounds";
            var soundsResult = cmd.ExecuteScalar();
            stats.SoundsPlayed = Convert.ToInt32(soundsResult ?? 0);
        }
        return stats;
    }

    public List<CelebrationHistory> GetRecentHistory(int limit = 50)
    {
        var result = new List<CelebrationHistory>();
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, image_id, trigger_type, shown_at, duration_ms FROM celebration_history ORDER BY id DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CelebrationHistory
            {
                Id = reader.GetInt64(0),
                ImageId = reader.IsDBNull(1) ? null : reader.GetString(1),
                TriggerType = reader.IsDBNull(2) ? null : reader.GetString(2),
                ShownAt = reader.GetString(3),
                DurationMs = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
        }
        return result;
    }

    /// <summary>
    /// Returns celebration counts bucketed by hour for chart rendering.
    /// Only counts entries with non-null shown_at timestamps.
    /// </summary>
    public List<TimeBucket> GetHistoryBuckets(DateTime sinceUtc, TimeSpan bucketSize)
    {
        var result = new List<TimeBucket>();
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT strftime('%Y-%m-%dT%H:%M', shown_at) as bucket, COUNT(*) as cnt
            FROM celebration_history
            WHERE shown_at IS NOT NULL
              AND shown_at >= $since
            GROUP BY strftime('%Y-%m-%dT%H:%M', shown_at)
            ORDER BY bucket ASC";
        cmd.Parameters.AddWithValue("$since", sinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        using var reader = cmd.ExecuteReader();
        var buckets = new Dictionary<DateTime, int>();
        while (reader.Read())
        {
            if (DateTime.TryParse(reader.GetString(0), out var ts))
            {
                var bucket = new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, 0, 0, DateTimeKind.Utc);
                buckets[bucket] = buckets.GetValueOrDefault(bucket) + reader.GetInt32(1);
            }
        }

        // Fill any gaps with zero counts for consistent graph data
        var current = sinceUtc;
        var end = DateTime.UtcNow;
        while (current <= end)
        {
            result.Add(new TimeBucket
            {
                Timestamp = current,
                Count = buckets.GetValueOrDefault(current, 0)
            });
            current = current.AddHours(1);
        }

        return result;
    }

    public List<CelebrationImage> GetEligibleImages(bool favoritesOnly)
    {
        var result = new List<CelebrationImage>();
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        var sql = $"SELECT {ImageColumns} FROM celebration_images WHERE enabled = 1 AND health != 'Deleted' AND (deleted_at IS NULL)";
        if (favoritesOnly)
        {
            sql += " AND favorite = 1";
        }
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadImage(reader));
        }
        return result;
    }

    // ---------------------------------------------------------------
    // Metadata
    // ---------------------------------------------------------------

    public string? GetMetadata(string key)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_metadata WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMetadata(string key, string value)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_metadata (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = $value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------
    // Sound clip CRUD
    // ---------------------------------------------------------------

    private const string SoundColumns = @"
            id, display_name, file_name, file_path, extension, file_size, created_at, sha256, times_played";

    public List<SoundClip> GetAllSoundClips()
    {
        var result = new List<SoundClip>();
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SoundColumns} FROM celebration_sounds ORDER BY created_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadSound(reader));
        }
        return result;
    }

    public SoundClip? GetSoundClip(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SoundColumns} FROM celebration_sounds WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSound(reader) : null;
    }

    public SoundClip? GetSoundClipBySha256(string sha256)
    {
        if (string.IsNullOrEmpty(sha256)) return null;
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SoundColumns} FROM celebration_sounds WHERE sha256 = $sha256 LIMIT 1";
        cmd.Parameters.AddWithValue("$sha256", sha256);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSound(reader) : null;
    }

    public void InsertSoundClip(SoundClip clip)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO celebration_sounds
                (id, display_name, file_name, file_path, extension, file_size, created_at, sha256, times_played)
            VALUES
                ($id, $display_name, $file_name, $file_path, $extension, $file_size, $created_at, $sha256, $times_played)";
        cmd.Parameters.AddWithValue("$id", clip.Id);
        cmd.Parameters.AddWithValue("$display_name", clip.DisplayName);
        cmd.Parameters.AddWithValue("$file_name", clip.FileName);
        cmd.Parameters.AddWithValue("$file_path", clip.FilePath);
        cmd.Parameters.AddWithValue("$extension", clip.Extension);
        cmd.Parameters.AddWithValue("$file_size", clip.FileSize);
        cmd.Parameters.AddWithValue("$created_at", clip.CreatedAt);
        cmd.Parameters.AddWithValue("$sha256", (object?)clip.Sha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$times_played", clip.TimesPlayed);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSoundClip(SoundClip clip)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE celebration_sounds SET
                display_name = $display_name,
                file_path = $file_path,
                times_played = $times_played
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", clip.Id);
        cmd.Parameters.AddWithValue("$display_name", clip.DisplayName);
        cmd.Parameters.AddWithValue("$file_path", clip.FilePath);
        cmd.Parameters.AddWithValue("$times_played", clip.TimesPlayed);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSoundClip(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM celebration_sounds WHERE id = $id;
            UPDATE celebration_images SET sound_id = NULL WHERE sound_id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void IncrementSoundTimesPlayed(string id)
    {
        using var connection = GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE celebration_sounds SET times_played = times_played + 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static SoundClip ReadSound(SqliteDataReader reader)
    {
        return new SoundClip
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            FileName = reader.GetString(2),
            FilePath = reader.GetString(3),
            Extension = reader.GetString(4),
            FileSize = reader.GetInt64(5),
            CreatedAt = reader.GetString(6),
            Sha256 = reader.IsDBNull(7) ? null : reader.GetString(7),
            TimesPlayed = reader.GetInt32(8)
        };
    }

    private static CelebrationImage ReadImage(SqliteDataReader reader)
    {
        return new CelebrationImage
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            FileName = reader.GetString(2),
            FilePath = reader.GetString(3),
            Category = reader.IsDBNull(4) ? null : reader.GetString(4),
            Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
            Enabled = reader.GetInt32(6) != 0,
            Favorite = reader.GetInt32(7) != 0,
            Weight = reader.GetInt32(8),
            TimesShown = reader.GetInt32(9),
            CreatedAt = reader.GetString(10),
            UpdatedAt = reader.GetString(11),
            LastShownAt = reader.IsDBNull(12) ? null : reader.GetString(12),
            Sha256 = reader.IsDBNull(13) ? null : reader.GetString(13),
            RemoteId = reader.IsDBNull(14) ? null : reader.GetString(14),
            StoragePath = reader.IsDBNull(15) ? null : reader.GetString(15),
            RemoteUpdatedAt = reader.IsDBNull(16) ? null : reader.GetString(16),
            LastSyncedAt = reader.IsDBNull(17) ? null : reader.GetString(17),
            MimeType = reader.IsDBNull(18) ? null : reader.GetString(18),
            FileSize = reader.IsDBNull(19) ? null : reader.GetInt64(19),
            Width = reader.IsDBNull(20) ? null : reader.GetInt32(20),
            Height = reader.IsDBNull(21) ? null : reader.GetInt32(21),
            Health = reader.IsDBNull(22) ? AssetHealth.Ready : (Enum.TryParse<AssetHealth>(reader.GetString(22), true, out var h) ? h : AssetHealth.Ready),
            IsLocalOnly = reader.GetInt32(23) != 0,
            DeletedAt = reader.IsDBNull(24) ? null : reader.GetString(24),
            CelebrationText = reader.IsDBNull(25) ? null : reader.GetString(25),
            SoundId = reader.IsDBNull(26) ? null : reader.GetString(26)
        };
    }

    private static void AddImageParameters(SqliteCommand cmd, CelebrationImage image)
    {
        cmd.Parameters.AddWithValue("$id", image.Id);
        cmd.Parameters.AddWithValue("$display_name", image.DisplayName);
        cmd.Parameters.AddWithValue("$file_name", image.FileName);
        cmd.Parameters.AddWithValue("$file_path", image.FilePath);
        cmd.Parameters.AddWithValue("$category", (object?)image.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", (object?)image.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", image.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$favorite", image.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$weight", image.Weight);
        cmd.Parameters.AddWithValue("$times_shown", image.TimesShown);
        cmd.Parameters.AddWithValue("$created_at", image.CreatedAt);
        cmd.Parameters.AddWithValue("$updated_at", image.UpdatedAt);
        cmd.Parameters.AddWithValue("$last_shown_at", (object?)image.LastShownAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sha256", (object?)image.Sha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$remote_id", (object?)image.RemoteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$storage_path", (object?)image.StoragePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$remote_updated_at", (object?)image.RemoteUpdatedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_synced_at", (object?)image.LastSyncedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mime_type", (object?)image.MimeType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$file_size", (object?)image.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$width", (object?)image.Width ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$height", (object?)image.Height ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$health", image.Health.ToString());
        cmd.Parameters.AddWithValue("$is_local_only", image.IsLocalOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$deleted_at", (object?)image.DeletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$celebration_text", (object?)image.CelebrationText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sound_id", (object?)image.SoundId ?? DBNull.Value);
    }
}
