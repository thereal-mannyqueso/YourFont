using FontBook.Core.Data;
using FontBook.Core.Services;
using FontBook.Infrastructure.Database;
using FontBook.Infrastructure.Fonts.Sample;
using Microsoft.Extensions.DependencyInjection;

namespace FontBook.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the database and font services implemented in this project.</summary>
    public static IServiceCollection AddFontBookInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton(_ =>
        {
            paths.EnsureCreated();
            return new SqliteConnectionFactory(paths.DatabasePath);
        });
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<IFavoritesRepository, SqliteFavoritesRepository>();
        services.AddSingleton<ICollectionRepository, SqliteCollectionRepository>();

        // TEMPORARY (Milestone 1): sample data instead of scanning the PC.
        // Milestone 2 replaces this line with the real Windows font discovery service.
        services.AddSingleton<IFontDiscoveryService, SampleFontDiscoveryService>();

        return services;
    }
}
