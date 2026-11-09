using FontBook.Core.Data;
using FontBook.Core.Services;
using FontBook.Infrastructure.Database;
using FontBook.Core.Vault;
using FontBook.Infrastructure.Fonts.Discovery;
using FontBook.Infrastructure.Fonts.Installation;
using FontBook.Infrastructure.Vault;
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
        services.AddSingleton<ICategoryRepository, SqliteCategoryRepository>();

        // Fonts installed in Windows (read-only: registry list + font files).
        services.AddSingleton<IInstalledFontFileSource, WindowsRegistryFontFileSource>();
        services.AddSingleton<IFontDiscoveryService, WindowsFontDiscoveryService>();

        // Per-user install/uninstall (documented Windows mechanism; never touches system fonts).
        services.AddSingleton<IUserFontRegistry, WindowsUserFontRegistry>();
        services.AddSingleton<IFontResourceApi, WindowsFontResourceApi>();
        services.AddSingleton<IFontInstallationService>(sp => new UserFontInstaller(
            UserFontInstaller.DefaultUserFontsDirectory,
            sp.GetRequiredService<IUserFontRegistry>(),
            sp.GetRequiredService<IFontResourceApi>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserFontInstaller>>()));

        // Font Vault (imported copies, kept apart from installed fonts).
        services.AddSingleton<SqliteVaultRepository>();
        services.AddSingleton<IFontVault, FontVaultService>();

        return services;
    }
}
