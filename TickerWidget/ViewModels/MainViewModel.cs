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
using System.Windows; // for Application.Current.Dispatcher fallback

namespace TickerWidget.ViewModels;

public enum PriceMovement
{
    Initial,   // første gang vi får en kurs (vises som sort)
    Up,        // kursen er steget siden sidst (vises som grøn)
    Down,      // kursen er faldet siden sidst (vises som rød)
    Unchanged  // uændret (vises som hvid)
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IYahooQuoteClient _yahoo;
    private readonly WidgetOptions _opt;

    // Timer der henter ALLE tickere hver gang (styres af PollingInterval)
    private readonly DispatcherTimer _fetchTimer = new();
    // Timer der roterer visningen (15 sek)
    private readonly DispatcherTimer _rotateTimer = new();

    private readonly Dispatcher _uiDispatcher;

    //private readonly string[] _tickers = new[] { "MSFT", "PLTR", "VWS.CO", "ISS.CO", "NETC.CO" };
    private readonly string[] _tickers = new[] { "MSFT", "PLTR", "LNAI" };
    private int _rotIdx;

    // Seneste priser (cache) fra sidste fetch
    private readonly Dictionary<string, DisplayQuote> _latest = new(StringComparer.OrdinalIgnoreCase);

    // Bindes i din ListBox – vi holder kun 1 linje synlig ad gangen
    public ObservableCollection<DisplayQuote> Items { get; } = new();

    // Flag der viser "Starter..." indtil første fetch er kørt
    private bool _isStarting = true;
    public bool IsStarting
    {
        get => _isStarting;
        private set
        {
            if (_isStarting == value) return;
            _isStarting = value;
            Raise();
        }
    }
    private bool _initialFetchDone;

    public MainViewModel(IYahooQuoteClient yahoo, IOptions<WidgetOptions> opt)
    {
        _yahoo = yahoo;
        _opt = opt.Value;

        // Capture UI dispatcher (assumes ViewModel constructed on UI thread)
        _uiDispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread) ?? (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);

        // === FETCH: hent alle tickere pr. interval ===
        _fetchTimer.Interval = _opt.PollingInterval;
        _fetchTimer.Tick += async (_, __) => await FetchAllAsync();

        // === ROTATE: skift vist ticker hvert 15. sekund ===
        _rotateTimer.Interval = TimeSpan.FromSeconds(15);
        _rotateTimer.Tick += (_, __) => RotateOnce();

        // Start begge timere og lav et initialt fetch/visning
        _fetchTimer.Start();
        _rotateTimer.Start();
        _ = FetchAllAsync(); // første hent nu (fire-and-forget)
    }

    private async Task FetchAllAsync()
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var tasks = new List<Task>();

            // First run: fetch all tickers regardless of market open, but set MarketOpen correctly per market time.
            if (!_initialFetchDone)
            {
                foreach (var t in _tickers)
                {
                    var code = GetMarketCode(t);
                    var tz = GetTimeZoneForMarket(code);
                    var marketNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
                    var marketHours = GetMarketHoursForTicker(t);
                    var marketOpen = marketHours.IsWithin(marketNow);

                    // fetch regardless, but let FetchOneAsync know whether market is open now
                    tasks.Add(FetchOneAsync(t, marketOpen));
                }
            }
            else
            {
                // Subsequent runs: only fetch tickers whose market is open; otherwise keep last known price but mark as closed.
                foreach (var t in _tickers)
                {
                    var code = GetMarketCode(t);
                    var tz = GetTimeZoneForMarket(code);
                    var marketNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
                    var marketHours = GetMarketHoursForTicker(t);

                    if (marketHours.IsWithin(marketNow))
                    {
                        tasks.Add(FetchOneAsync(t, true));
                    }
                    else
                    {
                        UpdateAsClosed(t, marketNow);
                    }
                }
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }
        catch
        {
            // TODO: log efter behov
        }
        finally
        {
            // efter første kørsel fjernes "Starter..."
            if (!_initialFetchDone)
            {
                _initialFetchDone = true;
                IsStarting = false;
            }
        }
    }

    // marketOpen indicates whether the market was open at fetch time; caller provides correct value.
    private async Task FetchOneAsync(string ticker, bool marketOpen = true)
    {
        try
        {
            var list = await _yahoo.GetIntradayAsync(ticker, interval: "15m", range: "1d");

            if (list is { Count: > 0 })
            {
                var last = list[^1];
                var rounded = decimal.Round((decimal)last.Close, 3);

                // Bestem bevægelsen ved at sammenligne med tidligere lagret kurs (hvis eksisterer)
                var movement = PriceMovement.Initial;
                if (_latest.TryGetValue(ticker, out var prev))
                {
                    if (rounded > prev.Price) movement = PriceMovement.Up;
                    else if (rounded < prev.Price) movement = PriceMovement.Down;
                    else movement = PriceMovement.Unchanged;
                }
                else
                {
                    movement = PriceMovement.Initial; // første gang vi ser ticker
                }

                _latest[ticker] = new DisplayQuote(
                    Ticker: ticker,
                    Price: rounded,
                    AsOf: last.TimestampUtc,
                    Movement: movement,
                    MarketOpen: marketOpen
                );

                // Ensure the UI updates immediately if this ticker is currently shown
                UpdateDisplayedIfNeeded(ticker);
            }
        }
        catch
        {
            // Ignorer per-ticker fejl; vi roterer videre på dem vi har
        }
    }

    private void UpdateAsClosed(string ticker, DateTimeOffset now)
    {
        // Preserve previous price/AsOf/movement if available; otherwise set defaults.
        if (_latest.TryGetValue(ticker, out var prev))
        {
            _latest[ticker] = prev with { MarketOpen = false };
        }
        else
        {
            // No previous price available — set placeholder but mark closed.
            _latest[ticker] = new DisplayQuote(
                Ticker: ticker,
                Price: 0m,
                AsOf: now,
                Movement: PriceMovement.Initial,
                MarketOpen: false
            );
        }

        // If the closed ticker is currently displayed, update UI immediately
        UpdateDisplayedIfNeeded(ticker);
    }

    private void UpdateDisplayedIfNeeded(string ticker)
    {
        void Action()
        {
            if (Items.Count == 1 && string.Equals(Items[0].Ticker, ticker, StringComparison.OrdinalIgnoreCase))
            {
                if (_latest.TryGetValue(ticker, out var dq))
                {
                    Items.Clear();
                    Items.Add(dq);
                }
            }
        }

        if (_uiDispatcher.CheckAccess())
        {
            Action();
        }
        else
        {
            _uiDispatcher.Invoke(Action);
        }
    }

    private void RotateOnce()
    {
        // Rotation still respects overall ActiveHours fallback if you want to hide rotation entirely outside global active hours:
        // if (!_opt.ActiveHours.IsWithin(DateTimeOffset.Now)) return;

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

    // Determine market code for a ticker. Simple heuristic; extend as needed.
    private static string GetMarketCode(string ticker)
    {
        if (ticker?.EndsWith(".CO", StringComparison.OrdinalIgnoreCase) == true)
            return "DK";
        return "US";
    }

    private ActiveHoursOptions GetMarketHoursForTicker(string ticker)
    {
        var code = GetMarketCode(ticker);
        if (_opt.Markets != null && _opt.Markets.TryGetValue(code, out var hours))
            return hours;
        return _opt.ActiveHours; // fallback
    }

    private static TimeZoneInfo GetTimeZoneForMarket(string marketCode)
    {
        try
        {
            return marketCode?.ToUpperInvariant() switch
            {
                "US" => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),   // Windows ID for ET
                "DK" => TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"), // Windows ID for Denmark (Copenhagen)
                _    => TimeZoneInfo.Local
            };
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed record DisplayQuote(string Ticker, decimal Price, DateTimeOffset AsOf, PriceMovement Movement, bool MarketOpen = true);
