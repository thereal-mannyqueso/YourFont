using FontBook.Core.Data;
using FontBook.Core.Services;
using FontBook.Infrastructure.Database;
using FontBook.Infrastructure.Fonts.Discovery;
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

        // Fonts installed in Windows (read-only: registry list + font files).
        services.AddSingleton<IInstalledFontFileSource, WindowsRegistryFontFileSource>();
        services.AddSingleton<IFontDiscoveryService, WindowsFontDiscoveryService>();

        return services;
    }
}
