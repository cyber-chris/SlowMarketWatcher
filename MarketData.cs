using System.Timers;

namespace SlowMarketWatcher
{
    public class MarketDataEventArgs : EventArgs
    {
        public string Message { get; set; }

        public MarketDataEventArgs(string message)
        {
            Message = message;
        }
    }

    public class MarketData
    {
        private static readonly HttpClient client = new HttpClient();
        private System.Timers.Timer aTimer;

        private string ApiKey;

        public event EventHandler<MarketDataEventArgs>? RaiseMarketDataEvent;

        public MarketData(string apiKey)
        {
            ApiKey = apiKey;

            aTimer = new System.Timers.Timer(60000);
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        private void OnTimedEvent(Object? source, ElapsedEventArgs e)
        {
            OnRaiseMarketDataEvent(new MarketDataEventArgs("Event triggered"));
        }

        protected virtual void OnRaiseMarketDataEvent(MarketDataEventArgs e)
        {
            // Since the event is null if no subscribers, copy the event and then check,
            // to avoid the race condition of someone unsubscribing after null check.
            var raiseEvent = RaiseMarketDataEvent;
            if (raiseEvent != null)
            {
                e.Message += $" at {DateTime.Now}";
                raiseEvent(this, e);
            }
        }
    }
}
