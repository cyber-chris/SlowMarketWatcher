// See https://aka.ms/new-console-template for more information
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                    services.AddHostedService<SlowMarketWatcherBot>(
                            provider => new SlowMarketWatcherBot(
                                provider.GetService<ILogger<SlowMarketWatcherBot>>() ?? throw new ArgumentNullException(),
                                provider.GetService<MarketData>() ?? throw new ArgumentNullException(),
                                Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_TOKEN") ?? throw new ArgumentNullException(),
                                storedIds
                            )
                        );
                    services.AddHttpClient<MarketData>();
                    services.AddSingleton<MarketData>(
                        provider => new MarketData(
                            provider.GetService<ILogger<MarketData>>() ?? throw new ArgumentNullException(),
                            provider.GetService<HttpClient>() ?? throw new ArgumentNullException(),
                            Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY") ?? throw new ArgumentNullException()
                        )
                    );
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
