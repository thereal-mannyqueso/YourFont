using FontBook.Core.Models;

namespace FontBook.Core.Services;

/// <summary>
/// Finds the fonts that FontBook should show in its Library.
/// </summary>
/// <remarks>
/// Milestone 1 ships only <c>SampleFontDiscoveryService</c>. Milestone 2 adds a real
/// implementation that reads the Windows font registry keys and font files.
/// Because the rest of the app only knows about this interface, swapping the
/// implementation is a one-line change in the dependency injection setup.
/// </remarks>
public interface IFontDiscoveryService
{
    /// <summary>True when results are placeholder data rather than fonts found on this PC.</summary>
    bool IsSampleData { get; }

    Task<IReadOnlyList<FontFamilyInfo>> DiscoverAsync(CancellationToken cancellationToken = default);
}
