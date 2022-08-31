// See https://aka.ms/new-console-template for more information
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Quartz.Impl;

namespace SlowMarketWatcher
{
    class Program
    {

        static async Task Main(string[] args)
        {
            DotEnv.Load();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((ctx, services) =>
                {
                    var configurationRoot = ctx.Configuration;
                    services.Configure<AlphaVantageSecret>(
                        configurationRoot.GetSection(nameof(AlphaVantageSecret))
                    );
                    services.Configure<TelegramSecret>(
                        configurationRoot.GetSection(nameof(TelegramSecret))
                    );

                    services.AddSingleton<MarketDataEvent>();
                    services.AddHttpClient<MarketData>();
                    services.AddHostedService<MarketData>();
                    services.AddHostedService<SlowMarketWatcherBot>();
                    services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
