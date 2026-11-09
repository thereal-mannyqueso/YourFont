using System.Windows;
using System.Windows.Threading;
using FontBook.App.Composition;
using FontBook.App.Services.Dialogs;
using FontBook.App.Services.Settings;
using FontBook.App.Services.Theming;
using FontBook.App.Views;
using FontBook.Core.Data;
using FontBook.Core.Models;
using FontBook.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FontBook.App;

/// <summary>
/// Application entry point: builds the services, prepares the database,
/// applies the saved theme and opens the main window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _services = AppServices.Build();
            _logger = _services.GetRequiredService<ILogger<App>>();

            await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

            // Apply the saved theme before the window appears, so it doesn't flash light first.
            var savedTheme = await _services.GetRequiredService<ISettingsStore>().GetAsync(SettingKeys.Theme);
            _services.GetRequiredService<IThemeService>()
                .Apply(Enum.TryParse<AppTheme>(savedTheme, out var theme) ? theme : AppTheme.System);

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // Start-up failures (e.g. an unreadable database) are fatal: explain and exit.
            _logger?.LogCritical(ex, "FontBook failed to start");
            MessageBox.Show(
                $"FontBook couldn't start.\n\n{ex.Message}",
                "FontBook",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            // Save anything still waiting in the settings buffer. FlushAsync never
            // resumes on the UI thread, so waiting for it here cannot deadlock.
            _services.GetRequiredService<DeferredSettingsWriter>().FlushAsync().GetAwaiter().GetResult();
            _services.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Last line of defense: log, tell the user, and keep the app running.
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        _services?.GetService<IDialogService>()?.ShowError(
            "Something went wrong",
            $"FontBook ran into an unexpected problem:\n\n{e.Exception.Message}");
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
