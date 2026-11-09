using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.Database;

/// <summary>
/// Creates the database on first run and upgrades older databases.
/// </summary>
/// <remarks>
/// SQLite stores a number called <c>user_version</c> inside the database file.
/// Each entry in <see cref="Migrations"/> upgrades the schema by one version.
/// On start-up we run every migration newer than the stored version, inside a
/// transaction, so an upgrade either fully succeeds or leaves the file untouched.
/// To change the schema later: append a new migration — never edit an old one.
/// </remarks>
public sealed class DatabaseInitializer(SqliteConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger)
{
    private static readonly string[] Migrations =
    [
        // Version 1 — Milestone 1 foundation.
        """
        CREATE TABLE settings (
            key   TEXT PRIMARY KEY NOT NULL,
            value TEXT NOT NULL
        );

        CREATE TABLE favorites (
            font_key TEXT PRIMARY KEY NOT NULL,
            added_at TEXT NOT NULL
        );

        CREATE TABLE collections (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL UNIQUE COLLATE NOCASE,
            created_at TEXT NOT NULL
        );

        CREATE TABLE collection_fonts (
            collection_id INTEGER NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
            font_key      TEXT NOT NULL,
            added_at      TEXT NOT NULL,
            PRIMARY KEY (collection_id, font_key)
        );

        CREATE INDEX ix_collection_fonts_font_key ON collection_fonts(font_key);
        """,

        // Version 2 — Font Vault: one row per imported file. The file itself lives in the
        // Vault folder under its content hash, so identical files are stored only once.
        """
        CREATE TABLE vault_files (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            sha256        TEXT NOT NULL UNIQUE,
            stored_name   TEXT NOT NULL,
            original_name TEXT NOT NULL,
            original_path TEXT NOT NULL,
            size_bytes    INTEGER NOT NULL,
            imported_at   TEXT NOT NULL
        );
        """,

        // Version 3 — categories the user chose by hand (overriding automatic detection).
        """
        CREATE TABLE font_categories (
            font_key   TEXT PRIMARY KEY NOT NULL,
            category   TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        """,
    ];

    /// <summary>The schema version this build of FontBook expects.</summary>
    public static int LatestVersion => Migrations.Length;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        // WAL journaling keeps the database consistent if the app crashes mid-write
        // and lets reads continue while a write is in progress.
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

        var current = await GetVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (current > LatestVersion)
        {
            throw new InvalidOperationException(
                $"The FontBook database is version {current}, but this version of FontBook only understands up to {LatestVersion}. " +
                "It was probably created by a newer FontBook release.");
        }

        for (var version = current + 1; version <= LatestVersion; version++)
        {
            logger.LogInformation("Upgrading database schema to version {Version}", version);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, Migrations[version - 1], cancellationToken, transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, $"PRAGMA user_version = {version};", cancellationToken, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task<int> GetVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
