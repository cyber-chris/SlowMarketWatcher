// See https://aka.ms/new-console-template for more information
using dotenv.net;
using Telegram.Bot;

DotEnv.Load();

var botClient = new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_TOKEN"));

var me = await botClient.GetMeAsync();
Console.WriteLine($"Hello, World! I am user {me.Id} and my name is {me.FirstName}.");
