using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SlowMarketWatcher
{
    /// Starts the Telegram client to listen for messages.
    /// When a message from a new chat ID is received, subscribe to the market data events and send a new message after receiving
    /// new market data.
    class SlowMarketWatcherBot : BackgroundService
    {
        private readonly ILogger<SlowMarketWatcherBot> _logger;

        private ITelegramBotClient botClient;
        private IDictionary<long, EventHandler<MarketDataEventArgs>> handlerDictionary;
        private MarketData eventPublisher;

        public SlowMarketWatcherBot(ILogger<SlowMarketWatcherBot> logger, MarketData publisher, string telegramAccessToken, IEnumerable<long> initialIds)
        {
            _logger = logger;
            botClient = new TelegramBotClient(telegramAccessToken);
            eventPublisher = publisher;
            handlerDictionary = new ConcurrentDictionary<long, EventHandler<MarketDataEventArgs>>();
            foreach (var id in initialIds)
            {
                EventHandler<MarketDataEventArgs> handler =
                    (sender, e) => botClient.SendTextMessageAsync(id, e.Message, parseMode: ParseMode.Markdown);
                var success = handlerDictionary.TryAdd(id, handler);
                if (!success)
                {
                    throw new Exception("Could not add id to dictionary.");
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token) {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };
            botClient.StartReceiving( // note that this does not block
                updateHandler: this.HandleUpdateAsync,
                pollingErrorHandler: this.HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: token
            );

            var me = await botClient.GetMeAsync();
            _logger.LogInformation($"Started {me.Username}");

            await Parallel.ForEachAsync(handlerDictionary.Keys,
                                        async (id, ct) => await botClient.SendTextMessageAsync(id, $"{me.Username} is active again!",
                                                                                               cancellationToken: ct));

            WaitHandle.WaitAny(new []{ token.WaitHandle });
        }

        public override async Task StopAsync(CancellationToken cancellationToken) {
            if (System.IO.Directory.Exists("/data"))
            {
                await System.IO.File.WriteAllLinesAsync("/data/clientIds", handlerDictionary.Keys.Select(id => id.ToString()), cancellationToken);
                _logger.LogInformation("Persisted {} ids.", handlerDictionary.Keys.Count);
            }

            foreach (var id in handlerDictionary.Keys)
            {
                await botClient.SendTextMessageAsync(id, "Bot is shutting down temporarily.", cancellationToken: cancellationToken);
            }

            await base.StopAsync(cancellationToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
                return;
            if (message.Text is not { } messageText)
                return;

            var chatId = message.Chat.Id;

            var replyMarkup = new ReplyKeyboardMarkup(new[] { new KeyboardButton("Start"), new KeyboardButton("Stop") }) { ResizeKeyboard = true };
            string toSend;
            switch (messageText.ToLower())
            {
                case "start":
                    {
                        if (handlerDictionary.ContainsKey(chatId))
                        {
                            toSend = $"Already activated this chat ({chatId}).";
                        }
                        else
                        {
                            EventHandler<MarketDataEventArgs> newHandler =
                                (sender, e) => botClient.SendTextMessageAsync(chatId, e.Message, parseMode: ParseMode.Markdown);
                            if (!handlerDictionary.TryAdd(chatId, newHandler))
                            {
                                throw new Exception("Should be able to add new handler.");
                            }
                            eventPublisher.RaiseMarketDataEvent += newHandler;
                            toSend = $"Subscribing... (Chat: {chatId})";
                            replyMarkup = new ReplyKeyboardMarkup(new KeyboardButton("Stop"));
                        }
                        break;
                    }
                case "stop":
                    {
                        if (handlerDictionary.TryGetValue(chatId, out var oldHandler))
                        {
                            eventPublisher.RaiseMarketDataEvent -= oldHandler;
                            handlerDictionary.Remove(chatId);
                            toSend = $"Unsubscribed you. (Chat: {chatId})";
                            replyMarkup = new ReplyKeyboardMarkup(new KeyboardButton("Start"));
                        }
                        else
                        {
                            toSend = $"Couldn't unsubscribe you. You probably aren't subscribed. (Chat: {chatId})";
                        }

                        break;
                    }
                default:
                    {
                        toSend = $"Unrecognised command: '{messageText}'";
                        break;
                    }
            }


            _logger.LogInformation($"Received a message '{messageText} in chat '{chatId}'");
            await botClient.SendTextMessageAsync(chatId: chatId, text: toSend, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"",
                _ => exception.ToString()
            };

            _logger.LogWarning(ErrorMessage);
            return Task.CompletedTask;
        }
    }
}
