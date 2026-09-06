using Microsoft.Data.Sqlite;

namespace JosephExperience.Data;

public static class DatabaseMigrations
{
    public const int CurrentSchemaVersion = 5;

    public static int GetSchemaVersion(SqliteConnection connection)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='app_metadata'";
            var table = check.ExecuteScalar();
            if (table is null || table is DBNull)
            {
                return 0;
            }
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_metadata WHERE key = 'schema_version'";
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            return 0;
        }
        return int.TryParse(result.ToString(), out var version) ? version : 0;
    }

    public static void Migrate(SqliteConnection connection)
    {
        var current = GetSchemaVersion(connection);

        if (current < 1)
        {
            ApplyInitialSchema(connection);
            SetSchemaVersion(connection, 1);
            current = 1;
        }

        if (current < 2)
        {
            ApplyV2Schema(connection);
            SetSchemaVersion(connection, 2);
            current = 2;
        }

        if (current < 3)
        {
            ApplyV3Schema(connection);
            SetSchemaVersion(connection, 3);
            current = 3;
        }

        if (current < 4)
        {
            ApplyV4Schema(connection);
            SetSchemaVersion(connection, 4);
            current = 4;
        }

        if (current < 5)
        {
            ApplyV5Schema(connection);
            SetSchemaVersion(connection, 5);
        }
    }

    private static void ApplyV5Schema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS celebration_sounds (
                id           TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                file_name    TEXT NOT NULL,
                file_path    TEXT NOT NULL,
                extension    TEXT NOT NULL,
                file_size    INTEGER NOT NULL DEFAULT 0,
                created_at   TEXT NOT NULL,
                sha256       TEXT,
                times_played INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_sounds_sha256 ON celebration_sounds(sha256);
        ";
        cmd.ExecuteNonQuery();

        AddColumnIfMissing(connection, "celebration_images", "sound_id", "TEXT");
    }

    private static void ApplyV4Schema(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "celebration_images", "celebration_text", "TEXT");
    }

    private static void ApplyV3Schema(SqliteConnection connection)
    {
        var columns = new (string name, string ddl)[]
        {
            ("mime_type", "TEXT"),
            ("file_size", "INTEGER"),
            ("width", "INTEGER"),
            ("height", "INTEGER"),
            ("health", "TEXT NOT NULL DEFAULT 'Ready'"),
            ("is_local_only", "INTEGER NOT NULL DEFAULT 0"),
            ("deleted_at", "TEXT")
        };
        foreach (var (name, ddl) in columns)
        {
            AddColumnIfMissing(connection, "celebration_images", name, ddl);
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_images_health ON celebration_images(health);";
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string ddl)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count == 0)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddl}";
            alter.ExecuteNonQuery();
        }
    }

    private static void ApplyV2Schema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            ALTER TABLE celebration_images ADD COLUMN remote_id TEXT;
            ALTER TABLE celebration_images ADD COLUMN storage_path TEXT;
            ALTER TABLE celebration_images ADD COLUMN remote_updated_at TEXT;
            ALTER TABLE celebration_images ADD COLUMN last_synced_at TEXT;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_images_remote_id ON celebration_images(remote_id);";
        cmd.ExecuteNonQuery();
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_metadata (key, value)
            VALUES ('schema_version', $version)
            ON CONFLICT(key) DO UPDATE SET value = $version";
        cmd.Parameters.AddWithValue("$version", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private static void ApplyInitialSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS celebration_images (
                id           TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                file_name    TEXT NOT NULL,
                file_path    TEXT NOT NULL,
                category     TEXT,
                tags         TEXT,
                enabled      INTEGER NOT NULL DEFAULT 1,
                favorite     INTEGER NOT NULL DEFAULT 0,
                weight       INTEGER NOT NULL DEFAULT 1,
                times_shown  INTEGER NOT NULL DEFAULT 0,
                created_at   TEXT NOT NULL,
                updated_at   TEXT NOT NULL,
                last_shown_at TEXT NULL,
                sha256       TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_images_enabled ON celebration_images(enabled);
            CREATE INDEX IF NOT EXISTS idx_images_favorite ON celebration_images(favorite);
            CREATE INDEX IF NOT EXISTS idx_images_sha256 ON celebration_images(sha256);

            CREATE TABLE IF NOT EXISTS celebration_history (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                image_id     TEXT,
                trigger_type TEXT,
                shown_at     TEXT NOT NULL,
                duration_ms  INTEGER,
                FOREIGN KEY (image_id) REFERENCES celebration_images(id)
            );

            CREATE INDEX IF NOT EXISTS idx_history_shown_at ON celebration_history(shown_at);
            CREATE INDEX IF NOT EXISTS idx_history_image ON celebration_history(image_id);

            CREATE TABLE IF NOT EXISTS app_metadata (
                key   TEXT PRIMARY KEY,
                value TEXT
            );";
        cmd.ExecuteNonQuery();
    }
}
