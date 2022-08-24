using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SlowMarketWatcher
{
    /// Starts the Telegram client to listen for messages.
    /// When a message from a new chat ID is received, subscribe to the market data events and send a new message after receiving
    /// new market data.
    class SlowMarketWatcherBot
    {
        private TelegramBotClient botClient;
        private ConcurrentBag<long> ids; // TODO: use hashmap from ids to delegate functions? i.e. to allow unsubscribe.
        private MarketData eventPublisher;

        public SlowMarketWatcherBot(string telegramAccessToken, MarketData publisher)
        {
            botClient = new TelegramBotClient(telegramAccessToken);
            ids = new ConcurrentBag<long>();
            eventPublisher = publisher;
        }

        public async Task StartAndRun()
        {
            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving( // note that this does not block
                updateHandler: this.HandleUpdateAsync,
                pollingErrorHandler: this.HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();

            Console.WriteLine($"Starting {me.Username}");
            Console.ReadLine();

            await Stop(botClient, cts);
        }

        private async Task Stop(ITelegramBotClient bot, CancellationTokenSource cts)
        {
            foreach (var id in ids)
            {
                await bot.SendTextMessageAsync(id, "Shutting down, you may need to reactivate me later.");
            }

            cts.Cancel();
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
                return;
            if (message.Text is not { } messageText)
                return;

            var chatId = message.Chat.Id;

            string toSend;
            switch (messageText.ToLower())
            {
                case "start":
                    {
                        if (ids.Contains(chatId))
                        {
                            toSend = $"Already activated this chat ({chatId}).";
                        }
                        else
                        {
                            eventPublisher.RaiseMarketDataEvent += (sender, e) => botClient.SendTextMessageAsync(chatId, e.Message);
                            toSend = $"Subscribing... (Chat: {chatId})";
                            ids.Add(chatId);
                        }
                        break;
                    }
                case "stop":
                    {
                        toSend = "Unimplemented";
                        break;
                    }
                default:
                    {
                        toSend = $"Unrecognised command: '{messageText}'";
                        break;
                    }
            }


            Console.WriteLine($"Received a message '{messageText} in chat '{chatId}'");
            await botClient.SendTextMessageAsync(chatId: chatId, text: toSend, cancellationToken: cancellationToken);
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
    }
}
