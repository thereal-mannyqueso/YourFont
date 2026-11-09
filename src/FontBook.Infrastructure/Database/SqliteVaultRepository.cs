namespace FontBook.Infrastructure.Database;

/// <summary>A file stored in the Font Vault.</summary>
/// <param name="StoredName">File name inside the Vault folder (content hash + extension).</param>
/// <param name="OriginalName">The file name the user imported, for display.</param>
public sealed record VaultFileRecord(
    long Id,
    string Sha256,
    string StoredName,
    string OriginalName,
    string OriginalPath,
    long SizeBytes,
    DateTimeOffset ImportedAt);

/// <summary>Database access for the Font Vault's file records.</summary>
public sealed class SqliteVaultRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<VaultFileRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sha256, stored_name, original_name, original_path, size_bytes, imported_at
            FROM vault_files ORDER BY imported_at;
            """;
        var result = new List<VaultFileRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new VaultFileRecord(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt64(5), DbTime.Parse(reader.GetString(6))));
        }

        return result;
    }

    public async Task<bool> ContainsHashAsync(string sha256, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM vault_files WHERE sha256 = $hash;";
        command.Parameters.AddWithValue("$hash", sha256);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task AddAsync(string sha256, string storedName, string originalName, string originalPath, long sizeBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO vault_files (sha256, stored_name, original_name, original_path, size_bytes, imported_at)
            VALUES ($hash, $stored, $name, $path, $size, $now);
            """;
        command.Parameters.AddWithValue("$hash", sha256);
        command.Parameters.AddWithValue("$stored", storedName);
        command.Parameters.AddWithValue("$name", originalName);
        command.Parameters.AddWithValue("$path", originalPath);
        command.Parameters.AddWithValue("$size", sizeBytes);
        command.Parameters.AddWithValue("$now", DbTime.Now());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vault_files WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
