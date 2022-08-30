using System.Text.Json;
using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quartz;

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

    public class MarketDataEvent
    {
        public event EventHandler<MarketDataEventArgs>? RaiseMarketDataEvent;

        public void OnRaiseMarketDataEvent(MarketDataEventArgs e)
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

    public class MarketData : BackgroundService
    {
        private readonly ILogger<MarketData> _logger;
        private readonly HttpClient _httpClient;
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly MarketDataEvent _marketDataEvent;

        private string ApiKey;
        private string[] symbols;

        public MarketData(ILogger<MarketData> logger, HttpClient httpClient, ISchedulerFactory schedulerFactory, MarketDataEvent marketDataEvent, string apiKey)
        {
            _logger = logger;
            _httpClient = httpClient;
            _schedulerFactory = schedulerFactory;
            _marketDataEvent = marketDataEvent;
            ApiKey = apiKey;
            symbols = new[] { "VEA", "VOO" };
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
#if (DEBUG)
            var cronExpr = "0 * * * * ?"; // every minute
#else
            var cronExpr = "0 0 9 * * ?"; // every 9:00
#endif

            var scheduler = await _schedulerFactory.GetScheduler();
            await scheduler.Start();

            var job = JobBuilder
                .Create<MarketDataJob>()
                .Build();
            // TODO: use Quartz DI
            job.JobDataMap.Put("httpClient", _httpClient);
            job.JobDataMap.Put("marketDataEvent", _marketDataEvent);
            job.JobDataMap.Put("symbols", symbols);
            job.JobDataMap.Put("apiKey", ApiKey);

            var trigger = TriggerBuilder.Create()
                .StartNow()
                .WithCronSchedule(cronExpr, x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")))
                .Build();

            await scheduler.ScheduleJob(job, trigger);
        }

        private void OnTimedEvent(Object? source, ElapsedEventArgs e)
        {
            foreach (var symbol in symbols)
            {
                var stringRes = _httpClient.GetStringAsync($"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={symbol}&outputsize=full&apikey={ApiKey}").Result;
                var response = JObject.Parse(stringRes);

                _marketDataEvent.OnRaiseMarketDataEvent(new MarketDataEventArgs(response));
            }
        }
    }

    public class MarketDataJob : IJob
    {
        public async Task Execute(IJobExecutionContext executionContext)
        {
            var httpClient = (HttpClient)executionContext.MergedJobDataMap.Get("httpClient");
            var marketDataEvent = (MarketDataEvent)executionContext.MergedJobDataMap.Get("marketDataEvent");
            var symbols = (string[])executionContext.MergedJobDataMap.Get("symbols");
            var apiKey = executionContext.MergedJobDataMap.Get("apiKey");

            foreach (var symbol in symbols)
            {
                var stringRes = await httpClient.GetStringAsync($"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={symbol}&outputsize=full&apikey={apiKey}");
                var response = JObject.Parse(stringRes);

                marketDataEvent.OnRaiseMarketDataEvent(new MarketDataEventArgs(response));
            }

        }
    }
}
