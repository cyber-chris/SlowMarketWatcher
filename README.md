# Slow Market Watcher (Telegram Bot)

Users can subscribe to the bot by sending `start`. Then, the bot will send periodic messages with useful/recent market summaries.

# Development

You need two secrets to run this yourself:
- `AlphaVantage__ApiKey` for the market data.
- `TelegramSecret__AccessToken` for the Telegram bot.
The format of these envvars is chosen to match what the .NET environment variable configuration provider expects.

This bot also acts as a convenient template for how you'd build any Telegram bot that sends periodic (currently daily) messages.
Just fork it and change the `MarketDataEventArgs` class to use whatever `Message` you want.
