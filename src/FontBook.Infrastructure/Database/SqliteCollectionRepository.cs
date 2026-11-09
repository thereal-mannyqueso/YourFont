using FontBook.Core.Collections;
using FontBook.Core.Data;
using FontBook.Core.Models;
using Microsoft.Data.Sqlite;

namespace FontBook.Infrastructure.Database;

public sealed class SqliteCollectionRepository(SqliteConnectionFactory connectionFactory) : ICollectionRepository
{
    private const int SqliteConstraintError = 19;

    public async Task<IReadOnlyList<FontCollection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, created_at FROM collections ORDER BY name COLLATE NOCASE;";
        var result = new List<FontCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new FontCollection(reader.GetInt64(0), reader.GetString(1), DbTime.Parse(reader.GetString(2))));
        }

        return result;
    }

    public async Task<FontCollection> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO collections (name, created_at) VALUES ($name, $now) RETURNING id;";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", DbTime.Format(now));
        try
        {
            var id = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            return new FontCollection(id, name, now);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
        {
            throw new CollectionNameTakenException(name);
        }
    }

    public async Task RenameAsync(long collectionId, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE collections SET name = $name WHERE id = $id;";
        command.Parameters.AddWithValue("$name", newName);
        command.Parameters.AddWithValue("$id", collectionId);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
        {
            throw new CollectionNameTakenException(newName);
        }
    }

    public async Task DeleteAsync(long collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // collection_fonts rows are removed automatically by ON DELETE CASCADE.
        command.CommandText = "DELETE FROM collections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", collectionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<string>> GetFontKeysAsync(long collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT font_key FROM collection_fonts WHERE collection_id = $id;";
        command.Parameters.AddWithValue("$id", collectionId);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<IReadOnlyList<CollectionMembership>> GetAllMembershipsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT collection_id, font_key FROM collection_fonts;";
        var result = new List<CollectionMembership>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new CollectionMembership(reader.GetInt64(0), reader.GetString(1)));
        }

        return result;
    }

    public async Task AddFontAsync(long collectionId, string fontKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO collection_fonts (collection_id, font_key, added_at)
            VALUES ($id, $key, $now);
            """;
        command.Parameters.AddWithValue("$id", collectionId);
        command.Parameters.AddWithValue("$key", fontKey);
        command.Parameters.AddWithValue("$now", DbTime.Now());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFontAsync(long collectionId, string fontKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM collection_fonts WHERE collection_id = $id AND font_key = $key;";
        command.Parameters.AddWithValue("$id", collectionId);
        command.Parameters.AddWithValue("$key", fontKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
