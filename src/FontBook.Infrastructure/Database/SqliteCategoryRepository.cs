using FontBook.Core.Data;
using FontBook.Core.Models;

namespace FontBook.Infrastructure.Database;

public sealed class SqliteCategoryRepository(SqliteConnectionFactory connectionFactory) : ICategoryRepository
{
    public async Task<IReadOnlyDictionary<string, FontCategory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT font_key, category FROM font_categories;";
        var result = new Dictionary<string, FontCategory>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Ignore values written by a future version that this build doesn't know.
            if (Enum.TryParse<FontCategory>(reader.GetString(1), out var category))
            {
                result[reader.GetString(0)] = category;
            }
        }

        return result;
    }

    public async Task SetAsync(string fontKey, FontCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO font_categories (font_key, category, updated_at) VALUES ($key, $category, $now)
            ON CONFLICT(font_key) DO UPDATE SET category = excluded.category, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", fontKey);
        command.Parameters.AddWithValue("$category", category.ToString());
        command.Parameters.AddWithValue("$now", DbTime.Now());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string fontKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM font_categories WHERE font_key = $key;";
        command.Parameters.AddWithValue("$key", fontKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
