using FontBook.App.Services.Dialogs;
using FontBook.App.Services.FontMaker;
using FontBook.App.Services.Rendering;
using FontBook.App.Services.Settings;
using FontBook.App.Services.Theming;
using FontBook.App.ViewModels;
using FontBook.App.Views;
using FontBook.Core.Collections;
using FontBook.Core.Library;
using FontBook.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FontBook.App.Composition;

/// <summary>
/// The one place where FontBook's pieces are wired together ("composition root").
/// Classes ask for interfaces in their constructors; this file decides which
/// concrete class each interface gets.
/// </summary>
public static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(TimeProvider.System);

        // Data and font services (FontBook.Infrastructure).
        services.AddFontBookInfrastructure(new AppPaths());

        // Business logic (FontBook.Core).
        services.AddSingleton<ICollectionService, CollectionService>();
        services.AddSingleton<IFontLibraryService, FontLibraryService>();

        // WPF-specific services (FontBook.App).
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IFontPreviewProvider, WpfFontPreviewProvider>();
        services.AddSingleton<DeferredSettingsWriter>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<IScanImageLoader, ScanImageLoader>();

        // View models and windows.
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<FontMakerViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
