// See https://aka.ms/new-console-template for more information
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;

namespace SlowMarketWatcher
{
    class Program
    {

        static async Task Main(string[] args)
        {
            DotEnv.Load();

            var storedIds = GetStoredIds();
            Console.WriteLine($"Found {storedIds.Count} stored ids.");

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<MarketDataEvent>();
                    services.AddHttpClient<MarketData>();
                    services.AddHostedService<MarketData>(
                        provider => new MarketData(
                            provider.GetService<ILogger<MarketData>>() ?? throw new ArgumentNullException(nameof(ILogger)),
                            provider.GetService<HttpClient>() ?? throw new ArgumentNullException(nameof(HttpClient)),
                            provider.GetService<ISchedulerFactory>() ?? throw new ArgumentNullException(nameof(ISchedulerFactory)),
                            provider.GetService<MarketDataEvent>() ?? throw new ArgumentNullException(nameof(MarketDataEvent)),
                            // TODO: look into IOptions for better DI
                            Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY") ?? throw new ArgumentNullException("API Key")
                        )
                    );
                    services.AddHostedService<SlowMarketWatcherBot>(
                            provider => new SlowMarketWatcherBot(
                                provider.GetService<ILogger<SlowMarketWatcherBot>>() ?? throw new ArgumentNullException(nameof(ILogger)),
                                provider.GetService<MarketDataEvent>() ?? throw new ArgumentNullException(nameof(MarketDataEvent)),
                                Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_TOKEN") ?? throw new ArgumentNullException("Access Token"),
                                storedIds
                            )
                        );
                    services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();
                })
                .Build();

            await host.RunAsync();
        }

        static IList<long> GetStoredIds()
        {
            var path = "/data/clientIds";
            var ids = new List<long>();
            if (System.IO.File.Exists(path))
            {
                foreach (var line in System.IO.File.ReadLines(path))
                {
                    ids.Add(long.Parse(line));
                }
            }
            return ids;
        }
    }
}
