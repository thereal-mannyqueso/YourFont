namespace FontBook.Core.Data;

/// <summary>Stores which font families the user marked as favorites (by <see cref="Models.FontKey"/>).</summary>
public interface IFavoritesRepository
{
    Task<IReadOnlySet<string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string fontKey, CancellationToken cancellationToken = default);

    Task RemoveAsync(string fontKey, CancellationToken cancellationToken = default);
}
