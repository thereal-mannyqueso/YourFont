using FontBook.Core.Models;

namespace FontBook.Core.Data;

/// <summary>Categories the user assigned by hand, keyed by <see cref="FontKey"/>.</summary>
public interface ICategoryRepository
{
    Task<IReadOnlyDictionary<string, FontCategory>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string fontKey, FontCategory category, CancellationToken cancellationToken = default);

    /// <summary>Removes the manual choice so automatic detection applies again.</summary>
    Task RemoveAsync(string fontKey, CancellationToken cancellationToken = default);
}
