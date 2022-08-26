using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
    class SlowMarketWatcherBot
    {
        private ITelegramBotClient botClient;
        private IDictionary<long, EventHandler<MarketDataEventArgs>> handlerDictionary;
        private MarketData eventPublisher;

        /// used to wake up the main thread upon signal, for a graceful shutdown
        private AutoResetEvent ctrlCEvent;

        public SlowMarketWatcherBot(string telegramAccessToken, MarketData publisher)
        {
            botClient = new TelegramBotClient(telegramAccessToken);
            handlerDictionary = new ConcurrentDictionary<long, EventHandler<MarketDataEventArgs>>();
            eventPublisher = publisher;
            ctrlCEvent = new AutoResetEvent(false);
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

            PosixSignalRegistration.Create(PosixSignal.SIGINT, SigintHandler);
            ctrlCEvent.WaitOne();

            await Stop(botClient, cts);
        }

        private void SigintHandler(PosixSignalContext ctx)
        {
            // effectively, this is a hacky way to wake up the main thread upon SIGINT, avoiding a spin loop
            ctx.Cancel = true;
            ctrlCEvent.Set();
        }

        private async Task Stop(ITelegramBotClient bot, CancellationTokenSource cts)
        {
            // TODO: persist ids
            foreach (var id in handlerDictionary.Keys)
            {
                await bot.SendTextMessageAsync(id, "Shutting down, you may need to reactivate me later.", replyMarkup: new ReplyKeyboardMarkup(new KeyboardButton("Start")));
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
                            EventHandler<MarketDataEventArgs> newHandler = (sender, e) => botClient.SendTextMessageAsync(chatId, e.Message, parseMode: ParseMode.Markdown);
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


            Console.WriteLine($"Received a message '{messageText} in chat '{chatId}'");
            await botClient.SendTextMessageAsync(chatId: chatId, text: toSend, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
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
