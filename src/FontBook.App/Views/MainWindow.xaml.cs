using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using FontBook.App.Services.Theming;
using FontBook.App.Services.Windowing;
using FontBook.App.ViewModels;

namespace FontBook.App.Views;

/// <summary>
/// Code-behind contains only window plumbing (backdrop, title bar color, maximize margins,
/// focus shortcuts). All app behavior lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IThemeService _themes;

    public MainWindow(MainViewModel viewModel, IThemeService themes)
    {
        _viewModel = viewModel;
        _themes = themes;
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizedMargin();
        _themes.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _themes.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        base.OnClosing(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (WindowEffects.TryEnableMica(this))
        {
            Background = Brushes.Transparent;
        }
        else
        {
            // Older Windows: keep the standard title bar and a solid background.
            WindowChrome.SetWindowChrome(this, null);
            AppTitle.Visibility = Visibility.Collapsed;
        }

        WindowEffects.SetDarkTitleBar(this, _themes.IsDark);
    }

    private void OnEffectiveThemeChanged(object? sender, EventArgs e) => WindowEffects.SetDarkTitleBar(this, _themes.IsDark);

    /// <summary>
    /// A maximized window with custom chrome extends a few pixels past the screen edges;
    /// pad the content by the resize border so nothing is cut off.
    /// </summary>
    private void UpdateMaximizedMargin()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        RootGrid.Margin = WindowState == WindowState.Maximized && chrome is not null
            ? SystemParameters.WindowResizeBorderThickness
            : new Thickness(0);
    }

    // ----- Drag and drop: shows the drop target and hands the dropped paths to the view model. -----

    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDrag(e);

    private void OnDragOver(object sender, DragEventArgs e) => UpdateDrag(e);

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            await _viewModel.ImportPathsAsync(paths);
        }
    }

    private void UpdateDrag(DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }
}
