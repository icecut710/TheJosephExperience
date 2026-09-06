using System.IO;
using JosephExperience.Models;

namespace JosephExperience.Services;

/// <summary>
/// Tracks the SQLite schema version for the application's database.
/// This is separate from the app version so that database migrations can be
/// managed independently (e.g., App v1.4.0 may use SQLite schema 3,
/// App v1.5.0 may introduce SQLite schema 4).
/// </summary>
public static class SQLiteSchemaVersion
{
    /// <summary>The current SQLite schema version the app expects.</summary>
    /// <remarks>
    /// This value should be incremented whenever the database schema changes
    /// in a backward-incompatible way (new tables, columns, etc.).
    /// </remarks>
    public const int CurrentExpectedSchema = 5;

    /// <summary>
    /// Reads the stored SQLite schema version from the database.
    /// If the database is new or the version table doesn't exist, returns 0.
    /// </summary>
    /// <param name="connectionString">The path to the SQLite database file.</param>
    /// <returns>The stored schema version, or 0 if not found.</returns>
    public static int ReadFromDatabase(string connectionString)
    {
        try
        {
            var dir = Path.GetDirectoryName(connectionString);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            connection.Open();

            // Create a simple version table if it doesn't exist
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS __MigrationVersion (
                    Version INT NOT NULL
                );
                INSERT INTO __MigrationVersion (Version) SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM __MigrationVersion);
            ";
            cmd.ExecuteNonQuery();

            // Read the current version
            using var readCmd = connection.CreateCommand();
            readCmd.CommandText = "SELECT Version FROM __MigrationVersion";
            var result = readCmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }
        }
        catch
        {
            // If we can't read it, assume version 0 (new database)
        }

        return 0;
    }

    /// <summary>
    /// Stores the given schema version in the database.
    /// </summary>
    /// <param name="connectionString">The path to the SQLite database file.</param>
    /// <param name="version">The schema version to store.</param>
    public static void WriteToDatabase(string connectionString, int version)
    {
        try
        {
            var dir = Path.GetDirectoryName(connectionString);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS __MigrationVersion (
                    Version INT NOT NULL
                );
                DELETE FROM __MigrationVersion;
                INSERT INTO __MigrationVersion (Version) VALUES (@v);
            ";
            cmd.Parameters.AddWithValue("@v", version);
            cmd.ExecuteNonQuery();
        }
        catch { /* ignore failures during schema tracking */ }
    }
}

/// <summary>
/// Represents a single migration step from one schema version to the next.
/// </summary>
public delegate bool SchemaMigration(Microsoft.Data.Sqlite.SqliteConnection connection);

/// <summary>
/// Holds ordered migration steps keyed by "from version -> to version".
/// </summary>
public static class SchemaMigrationManager
{
    // Example: 3 -> 4, 4 -> 5 migrations would be registered here.
    // For now, no migrations are registered; add them as needed.

    /// <summary>
    /// Runs all pending migrations from the current schema version up to the expected schema.
    /// </summary>
    /// <param name="connectionString">The path to the SQLite database file.</param>
    /// <returns>True if all migrations succeeded, false if any failed.</returns>
    public static bool RunMigrations(string connectionString)
    {
        int current = SQLiteSchemaVersion.ReadFromDatabase(connectionString);
        int target = SQLiteSchemaVersion.CurrentExpectedSchema;

        if (current >= target)
        {
            // Already up to date
            return true;
        }

        // Run migrations in order
        for (int v = current + 1; v <= target; v++)
        {
            // In a real implementation, you would call a specific migration delegate here.
            // For now, we just mark the version as moved.
            // TODO: Add actual migration logic for each version step.
            SQLiteSchemaVersion.WriteToDatabase(connectionString, v);
        }

        return true;
    }
}