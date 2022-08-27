// See https://aka.ms/new-console-template for more information
using dotenv.net;

namespace SlowMarketWatcher
{
    class Program
    {

        static async Task Main(string[] args)
        {
            DotEnv.Load();

            var storedIds = GetStoredIds();
            Console.WriteLine($"Found {storedIds.Count} stored ids.");

            var marketData = new MarketData(Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY") ?? throw new ArgumentNullException());
            var bot = new SlowMarketWatcherBot(Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_TOKEN") ?? throw new ArgumentNullException(), marketData, storedIds);
            await bot.StartAndRun();
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
