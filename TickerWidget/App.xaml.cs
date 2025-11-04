using Data.Abstractions.Providers.Prices;
using Data.Providers;
using Data.Providers.Prices;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Windows;
using TickerWidget.Options;
using TickerWidget.ViewModels;


namespace TickerWidget;

public partial class App : Application
{
    public static IHost HostApp { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        HostApp = Host.CreateDefaultBuilder()
          .ConfigureAppConfiguration(cfg => cfg
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<App>(optional: true)
            .AddEnvironmentVariables())
          .ConfigureServices((ctx, services) =>
          {
              services.AddOptions<WidgetOptions>()
                      .Bind(ctx.Configuration.GetSection("Widget"))                
                      .Validate(o => o.PollingInterval > TimeSpan.Zero, "PollingInterval must be > 0")
                      .Validate(o => o.ActiveHours is not null, "ActiveHours required")
                      .Validate(o => o.ActiveHours.End != o.ActiveHours.Start, "ActiveHours Start/End cannot be equal")                      
                      .ValidateOnStart();

              services.AddDataProviders(ctx.Configuration);

              services.AddSingleton<MainViewModel>();

              
          })
          .Build();

        HostApp.Start();

        var win = new MainWindow
        {
            DataContext = HostApp.Services.GetRequiredService<MainViewModel>()
        };
        win.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await HostApp.StopAsync();
        HostApp.Dispose();
        base.OnExit(e);
    }
}
