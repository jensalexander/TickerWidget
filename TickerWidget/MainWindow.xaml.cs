using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickerWidget.ViewModels;

namespace TickerWidget;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Ensure timers are stopped if the window is closed by other means (Alt+F4, taskbar, etc.)
        this.Closing += MainWindow_Closing;
    }

    // Allow dragging the window by mouse from the Grid background.
    // Avoid starting DragMove when the click originates from controls like the Close button.
    private void Grid_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        // If the original source (or any of its visual ancestors) is a Button,
        // skip DragMove so button clicks still work.
        if (e.OriginalSource is DependencyObject dobj && IsAncestorOfType<Button>(dobj))
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if called in an invalid state; ignore safely.
        }
    }

    // Close button handler
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        // optional: quick sanity log for debugging
        // System.Diagnostics.Debug.WriteLine("CloseButton_Click");

        if (DataContext is MainViewModel vm)
        {
            vm.Stop(); // stop timers before closing
        }

        Close();
    }

    // Also stop timers if window is closed by other means
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Stop();
        }
    }

    // Walk up the visual tree to see if any ancestor is of the requested type.
    private static bool IsAncestorOfType<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current != null)
        {
            if (current is T) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}
