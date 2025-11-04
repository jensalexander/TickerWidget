using TickerWidget.Options;
using Data.Abstractions.Providers.Prices;
using Microsoft.Extensions.Options;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TickerWidget.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IYahooQuoteClient _yahoo;
    private readonly WidgetOptions _opt;

    // Timer der henter ALLE tickere hver gang (styres af PollingInterval)
    private readonly DispatcherTimer _fetchTimer = new();
    // Timer der roterer visningen (15 sek)
    private readonly DispatcherTimer _rotateTimer = new();

    private readonly string[] _tickers = new[] { "MSFT", "PLTR", "VWS.CO", "ISS.CO", "NETC.CO" };
    private int _rotIdx;

    // Seneste priser (cache) fra sidste fetch
    private readonly Dictionary<string, DisplayQuote> _latest = new(StringComparer.OrdinalIgnoreCase);

    // Bindes i din ListBox – vi holder kun 1 linje synlig ad gangen
    public ObservableCollection<DisplayQuote> Items { get; } = new();

    public MainViewModel(IYahooQuoteClient yahoo, IOptions<WidgetOptions> opt)
    {
        _yahoo = yahoo;
        _opt = opt.Value;

        // === FETCH: hent alle tickere pr. interval ===
        _fetchTimer.Interval = _opt.PollingInterval;
        _fetchTimer.Tick += async (_, __) => await FetchAllAsync();

        // === ROTATE: skift vist ticker hvert 15. sekund ===
        _rotateTimer.Interval = TimeSpan.FromSeconds(15);
        _rotateTimer.Tick += (_, __) => RotateOnce();

        // Start begge timere og lav et initialt fetch/visning
        _fetchTimer.Start();
        _rotateTimer.Start();
        _ = FetchAllAsync(); // første hent nu
    }

    private async Task FetchAllAsync()
    {
        if (!_opt.ActiveHours.IsWithin(DateTimeOffset.Now))
            return;

        try
        {
            // Hent hver ticker (paralleliseret)
            var tasks = new List<Task>((_tickers.Length));
            foreach (var t in _tickers)
            {
                tasks.Add(FetchOneAsync(t));
            }
            await Task.WhenAll(tasks);
        }
        catch
        {
            // TODO: log efter behov
        }
    }

    private async Task FetchOneAsync(string ticker)
    {
        try
        {
            var list = await _yahoo.GetIntradayAsync(ticker, interval: "15m", range: "1d");
            if (list is { Count: > 0 })
            {
                var last = list[^1];
                _latest[ticker] = new DisplayQuote(
                    Ticker: ticker,
                    Price: decimal.Round((decimal)last.Close, 3),
                    AsOf: last.TimestampUtc
                );
            }
        }
        catch
        {
            // Ignorer per-ticker fejl; vi roterer videre på dem vi har
        }
    }

    private void RotateOnce()
    {
        if (!_opt.ActiveHours.IsWithin(DateTimeOffset.Now))
            return;

        if (_tickers.Length == 0) return;

        var t = _tickers[_rotIdx++ % _tickers.Length];

        if (_latest.TryGetValue(t, out var dq))
        {
            Items.Clear();          // vis kun én ad gangen
            Items.Add(dq);
        }
        // Hvis vi ikke har data for den ticker endnu, skipper vi bare
    }

    // Manuelle kontroller hvis du vil bruge dem fra UI
    public void Start()
    {
        _fetchTimer.Start();
        _rotateTimer.Start();
    }
    public void Stop()
    {
        _fetchTimer.Stop();
        _rotateTimer.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed record DisplayQuote(string Ticker, decimal Price, DateTimeOffset AsOf);
