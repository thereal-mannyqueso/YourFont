using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FontBook.App.Behaviors;

/// <summary>
/// Small attached behaviors for text boxes, used by the inline collection rename box.
/// They are pure UI plumbing (focus and key handling), which is why they live here
/// instead of in a view model. All commands receive <c>CommandParameter</c>.
/// </summary>
public static class TextBoxBehaviors
{
    /// <summary>When true, the text box takes keyboard focus and selects its text whenever it becomes visible.</summary>
    public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusWhenVisible", typeof(bool), typeof(TextBoxBehaviors), new PropertyMetadata(false, OnFocusWhenVisibleChanged));

    /// <summary>Executed when Enter is pressed.</summary>
    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(ICommand), typeof(TextBoxBehaviors), new PropertyMetadata(null, OnKeyCommandChanged));

    /// <summary>Executed when Escape is pressed.</summary>
    public static readonly DependencyProperty EscapeCommandProperty = DependencyProperty.RegisterAttached(
        "EscapeCommand", typeof(ICommand), typeof(TextBoxBehaviors), new PropertyMetadata(null, OnKeyCommandChanged));

    /// <summary>Executed when the text box loses keyboard focus.</summary>
    public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.RegisterAttached(
        "LostFocusCommand", typeof(ICommand), typeof(TextBoxBehaviors), new PropertyMetadata(null, OnLostFocusCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(TextBoxBehaviors), new PropertyMetadata(null));

    public static bool GetFocusWhenVisible(DependencyObject element) => (bool)element.GetValue(FocusWhenVisibleProperty);

    public static void SetFocusWhenVisible(DependencyObject element, bool value) => element.SetValue(FocusWhenVisibleProperty, value);

    public static ICommand? GetEnterCommand(DependencyObject element) => (ICommand?)element.GetValue(EnterCommandProperty);

    public static void SetEnterCommand(DependencyObject element, ICommand? value) => element.SetValue(EnterCommandProperty, value);

    public static ICommand? GetEscapeCommand(DependencyObject element) => (ICommand?)element.GetValue(EscapeCommandProperty);

    public static void SetEscapeCommand(DependencyObject element, ICommand? value) => element.SetValue(EscapeCommandProperty, value);

    public static ICommand? GetLostFocusCommand(DependencyObject element) => (ICommand?)element.GetValue(LostFocusCommandProperty);

    public static void SetLostFocusCommand(DependencyObject element, ICommand? value) => element.SetValue(LostFocusCommandProperty, value);

    public static object? GetCommandParameter(DependencyObject element) => element.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject element, object? value) => element.SetValue(CommandParameterProperty, value);

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        textBox.IsVisibleChanged -= OnIsVisibleChanged;
        if (e.NewValue is true)
        {
            textBox.IsVisibleChanged += OnIsVisibleChanged;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && e.NewValue is true)
        {
            // Wait until layout has finished, otherwise Focus() can be ignored.
            textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                textBox.Focus();
                Keyboard.Focus(textBox);
                textBox.SelectAll();
            });
        }
    }

    private static void OnKeyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.PreviewKeyDown -= OnPreviewKeyDown;
            element.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Enter => GetEnterCommand(element),
            Key.Escape => GetEscapeCommand(element),
            _ => null,
        };

        if (command is not null && Execute(command, GetCommandParameter(element)))
        {
            e.Handled = true;
        }
    }

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.LostKeyboardFocus -= OnLostKeyboardFocus;
        if (e.NewValue is not null)
        {
            element.LostKeyboardFocus += OnLostKeyboardFocus;
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DependencyObject element && GetLostFocusCommand(element) is { } command)
        {
            Execute(command, GetCommandParameter(element));
        }
    }

    private static bool Execute(ICommand command, object? parameter)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}
