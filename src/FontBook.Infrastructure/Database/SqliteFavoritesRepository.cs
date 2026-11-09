using FontBook.Core.Data;

namespace FontBook.Infrastructure.Database;

public sealed class SqliteFavoritesRepository(SqliteConnectionFactory connectionFactory) : IFavoritesRepository
{
    public async Task<IReadOnlySet<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT font_key FROM favorites;";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task AddAsync(string fontKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO favorites (font_key, added_at) VALUES ($key, $now);";
        command.Parameters.AddWithValue("$key", fontKey);
        command.Parameters.AddWithValue("$now", DbTime.Now());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string fontKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM favorites WHERE font_key = $key;";
        command.Parameters.AddWithValue("$key", fontKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
