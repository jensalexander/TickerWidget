using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickerWidget.ViewModels;

namespace TickerWidget
{
    public partial class MainWindow : Window
    {
        private bool _fixedWidthSet;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            DataContextChanged += MainWindow_DataContextChanged;
            Card.SizeChanged += Card_SizeChanged;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            HookCollectionChanged();
            // Set fixed width once based on known tickers/marketcodes
            SetFixedWidthFromViewModel();
            UpdateClip();
        }

        private void MainWindow_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            HookCollectionChanged();
            // ensure width calculation once when DataContext becomes available
            if (!_fixedWidthSet)
                Dispatcher.BeginInvoke(new Action(SetFixedWidthFromViewModel));
        }

        private void HookCollectionChanged()
        {
            var src = ItemsListBox.ItemsSource as INotifyCollectionChanged;
            if (src == null) return;
            src.CollectionChanged -= ItemsSource_CollectionChanged;
            src.CollectionChanged += ItemsSource_CollectionChanged;
        }

        private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // keep clip updated if items change, but do not resize window
            Dispatcher.BeginInvoke(new Action(UpdateClip));
        }

        private void Card_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdateClip();
        }

        // Compute a fixed Width at startup based on ViewModel's known tickers + market codes.
        private void SetFixedWidthFromViewModel()
        {
            if (_fixedWidthSet) return;

            MainViewModel? vm = DataContext as MainViewModel;

            if (vm == null || vm.Tickers == null || vm.Tickers.Count == 0)
            {
                // fallback: measure whatever items currently in the list
                if (ItemsListBox == null || ItemsListBox.Items.Count == 0) return;
            }

            // Helper to measure text width using the same FontFamily as window
            double MeasureTextWidth(string text, double fontSize)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontFamily = this.FontFamily,
                    FontWeight = FontWeights.Normal
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return tb.DesiredSize.Width;
            }

            double maxBadgeArea = 0.0;

            var tickers = vm?.Tickers ?? Array.Empty<string>();
            foreach (var t in tickers)
            {
                var marketCode = t?.EndsWith(".CO", StringComparison.OrdinalIgnoreCase) == true ? "DK" : "US";

                // ticker badge: font 12 + border padding 6 left + 6 right => +12
                var tickerTextWidth = MeasureTextWidth(t ?? string.Empty, 12) + 12;

                // market badge: font 11 + border padding 6 left + 6 right => +12
                var marketTextWidth = MeasureTextWidth(marketCode, 11) + 12;

                // spacing between badges: Margin on market border = 6
                var badgesTotal = tickerTextWidth + 6 + marketTextWidth;
                maxBadgeArea = Math.Max(maxBadgeArea, badgesTotal);
            }

            // reserve width for price + timestamp (measured with size 14)
            var priceSample = "12345.678"; // conservative sample for price width
            var priceWidth = MeasureTextWidth(priceSample, 14);
            var timeSample = "2000-12-31 23:59:59";
            var timeWidth = MeasureTextWidth(timeSample, 14);

            // list padding + grid margins + close button width + spacing + safety buffer
            double extra = 12 /*list padding left*/ + 8 /*list padding right*/ + 8 /*outer grid margin*/ + 26 /*close button width*/ + 8 /*button margin*/ + 24 /*safety buffer*/;

            // Space between badges and price = margin on price TextBlock (right of price is 12) but left spacing between badges and price is implicit; include 12
            double gapBetweenBadgesAndPrice = 12;

            var targetWidth = Math.Ceiling(maxBadgeArea + gapBetweenBadgesAndPrice + priceWidth + 12 /*price right margin*/ + timeWidth + extra);

            // Respect screen/work area
            var screenWidth = SystemParameters.WorkArea.Width;
            if (targetWidth > screenWidth) targetWidth = (int)screenWidth;

            Width = Math.Max(MinWidth, targetWidth);
            _fixedWidthSet = true;
            UpdateClip();
        }

        private void UpdateClip()
        {
            if (CardClip == null || Card == null) return;
            CardClip.Rect = new Rect(0, 0, Math.Max(0, Card.ActualWidth), Math.Max(0, Card.ActualHeight));
        }

        // existing event handlers referenced from XAML (preserve if already implemented elsewhere)
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void Grid_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    }
}
