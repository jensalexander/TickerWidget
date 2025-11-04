using System.Windows;
using System.Windows.Input;

namespace TickerWidget;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
