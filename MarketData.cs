using System.Text.Json;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SlowMarketWatcher
{
    public class MarketDataEventArgs : EventArgs
    {
        public string Message { get; set; }

        public MarketDataEventArgs(JObject timeSeriesDailyResponse)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var timeSeriesDaily = timeSeriesDailyResponse["Time Series (Daily)"].ToObject<JObject>();
            while (!timeSeriesDaily.ContainsKey(today.ToString("yyyy-MM-dd")))
            {
                today = today.AddDays(-1);
            }

            var symbol = timeSeriesDailyResponse["Meta Data"]["2. Symbol"].ToString();
            var closeVal = timeSeriesDaily[today.ToString("yyyy-MM-dd")]["4. close"];

            var outputMessage = "";
            outputMessage += $"\n{symbol} close on {today}: {closeVal}";
            outputMessage += $"\n14 period SMA: {SimpleMovingAverage(today, timeSeriesDaily)}";
            Message = outputMessage;

            // TODO:
            // - RSI
        }

        public MarketDataEventArgs(string message)
        {
            Message = message;
        }

        /// Returns value rounded to 2dp.
        private double SimpleMovingAverage(DateOnly mostRecentDataDate, in JObject timeSeriesDaily, int days = 14)
        {
            var closeSum = 0.0;
            for (var i = 0; i <= days; i++)
            {
                closeSum += timeSeriesDaily[mostRecentDataDate.ToString("yyyy-MM-dd")]["4. close"].ToObject<double>();
                do
                {
                    mostRecentDataDate = mostRecentDataDate.AddDays(-1);
                } while (!timeSeriesDaily.ContainsKey(mostRecentDataDate.ToString("yyyy-MM-dd")));
            }
            return Math.Round(closeSum / days, 2);
        }
    }

    public class TimeSeriesDailyResponse
    {
        [JsonProperty("Meta Data")]
        public IDictionary<string, string>? MetaData { get; }
        [JsonProperty("Time Series (Daily)")]
        public IDictionary<string, IDictionary<string, string>>? TimeSeriesDaily { get; }
    }

    public class MarketData
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private System.Timers.Timer aTimer;

        private string ApiKey;
        private string[] symbols;

        public event EventHandler<MarketDataEventArgs>? RaiseMarketDataEvent;


        public MarketData(string apiKey)
        {
            ApiKey = apiKey;
            symbols = new[] { "VEA", "VOO" };

#if (DEBUG)
            var interval = 60000;
#else
            var interval = 60000 * 60 * 24;
#endif

            aTimer = new System.Timers.Timer(interval);
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        private void OnTimedEvent(Object? source, ElapsedEventArgs e)
        {
            foreach (var symbol in symbols)
            {
                var stringRes = httpClient.GetStringAsync($"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={symbol}&outputsize=full&apikey={ApiKey}").Result;
                var response = JObject.Parse(stringRes);

                OnRaiseMarketDataEvent(new MarketDataEventArgs(response));
            }
        }

        protected virtual void OnRaiseMarketDataEvent(MarketDataEventArgs e)
        {
            // Since the event is null if no subscribers, copy the event and then check,
            // to avoid the race condition of someone unsubscribing after null check.
            var raiseEvent = RaiseMarketDataEvent;
            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
        }
    }
}
