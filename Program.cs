// See https://aka.ms/new-console-template for more information
using dotenv.net;

namespace SlowMarketWatcher
{
    class Program
    {

        static async Task Main(string[] args)
        {
            DotEnv.Load();

            var marketData = new MarketData(Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY") ?? throw new ArgumentNullException());
            var bot = new SlowMarketWatcherBot(Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_TOKEN") ?? throw new ArgumentNullException(), marketData);
            await bot.StartAndRun();
        }
    }
}
