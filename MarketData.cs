using System.Text.Json;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SlowMarketWatcher
{
    public class MarketDataEventArgs : EventArgs
    {
        public string Message { get; set; }

        public MarketDataEventArgs(JObject timeSeriesDailyResponse) {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var timeSeriesDaily = timeSeriesDailyResponse["Time Series (Daily)"].ToObject<JObject>();
            while (!timeSeriesDaily.ContainsKey(today.ToString("yyyy-MM-dd"))) {
                today = today.AddDays(-1);
            }

            var symbol = timeSeriesDailyResponse["Meta Data"]["2. Symbol"].ToString();
            var closeVal = timeSeriesDaily[today.ToString("yyyy-MM-dd")]["4. close"];
            Message = $"{symbol} closed {closeVal} at {today}";

            // TODO:
            // - 7 day SMA
            // - RSI
        }

        public MarketDataEventArgs(string message)
        {
            Message = message;
        }
    }

    public class TimeSeriesDailyResponse {
        [JsonProperty("Meta Data")]
        public IDictionary<string, string> MetaData { get; }
        [JsonProperty("Time Series (Daily)")]
        public IDictionary<string, IDictionary<string, string>> TimeSeriesDaily { get; }
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
            symbols = new [] { "VEA", "VOO" };

            var interval = 60000;
            aTimer = new System.Timers.Timer(interval);
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        private void OnTimedEvent(Object? source, ElapsedEventArgs e)
        {
            foreach (var symbol in symbols) {
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
