using System.Text.Json;
using System.Timers;
using Microsoft.Extensions.Logging;
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

            var days = 14;
            var outputMessage = $"*{symbol}*";
            outputMessage += $"\nClose on {today}: {closeVal}";
            outputMessage += $"\n{days} period SMA: {SimpleMovingAverage(today, timeSeriesDaily, days)}";
            outputMessage += $"\n{days} period RSI: {RelativeStrengthIndex(today, timeSeriesDaily, days)}";
            Message = outputMessage;
        }

        public MarketDataEventArgs(string message)
        {
            Message = message;
        }

        /// Returns the most recent date before the current date where we have trading data.
        private DateOnly PrevDate(DateOnly currentDate, in JObject timeSeriesDaily)
        {
            do
            {
                currentDate = currentDate.AddDays(-1);
            } while (!timeSeriesDaily.ContainsKey(currentDate.ToString("yyyy-MM-dd")));
            return currentDate;
        }

        /// Returns value rounded to 2dp.
        private double SimpleMovingAverage(DateOnly mostRecentDataDate, in JObject timeSeriesDaily, int lookbackDays = 14)
        {
            var closeSum = 0.0;
            for (var i = 0; i <= lookbackDays; i++)
            {
                closeSum += timeSeriesDaily[mostRecentDataDate.ToString("yyyy-MM-dd")]["4. close"].ToObject<double>();
                mostRecentDataDate = PrevDate(mostRecentDataDate, timeSeriesDaily);
            }
            return Math.Round(closeSum / lookbackDays, 2);
        }

        private double RelativeStrengthIndex(DateOnly mostRecentDataDate, in JObject timeSeriesDaily, int lookbackDays = 14)
        {
            var upwardSum = 0.0;
            var downwardSum = 0.0;
            for (var i = 0; i <= lookbackDays; i++)
            {
                var prev = PrevDate(mostRecentDataDate, timeSeriesDaily);
                var prevClose = timeSeriesDaily[prev.ToString("yyyy-MM-dd")]["4. close"].ToObject<double>();
                var currClose = timeSeriesDaily[mostRecentDataDate.ToString("yyyy-MM-dd")]["4. close"].ToObject<double>();
                if (prevClose <= currClose)
                {
                    upwardSum += currClose - prevClose;
                }
                else
                {
                    downwardSum += prevClose - currClose;
                }

                mostRecentDataDate = prev;
            }
            var averageGain = upwardSum / lookbackDays;
            var averageLoss = downwardSum / lookbackDays;
            if (averageLoss == 0)
            {
                return 100.0; // because the relative strength will approach 0 so RSI will approach 100
            }
            var relativeStrength = averageGain / averageLoss;
            return Math.Round(100 - (100 / (1 + relativeStrength)), 2);
        }
    }

    /// Model for the JSON response.
    public record TimeSeriesDailyResponse(
        [JsonProperty("Meta Data")]
        IDictionary<string, string> MetaData,
        [JsonProperty("Time Series (Daily)")]
        IDictionary<string, IDictionary<string, string>> TimeSeriesDaily
    );

    public class MarketData
    {
        private readonly ILogger<MarketData> _logger;
        private readonly HttpClient _httpClient;
        private System.Timers.Timer aTimer; // TODO: it would be more sensible to have a fixed time per day

        private string ApiKey;
        private string[] symbols;

        public event EventHandler<MarketDataEventArgs>? RaiseMarketDataEvent;

        public MarketData(ILogger<MarketData> logger, HttpClient httpClient, string apiKey)
        {
            _logger = logger;
            _httpClient = httpClient;
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
                var stringRes = _httpClient.GetStringAsync($"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={symbol}&outputsize=full&apikey={ApiKey}").Result;
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
