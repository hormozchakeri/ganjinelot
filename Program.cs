using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static readonly string BotToken = "8116064464:AAF126JbFdrwJAtE3KjKBX-NNVjIEFOu-t4";
    private static readonly long AdminChatId = hormozchakeri; // Replace with your admin Telegram ID
    private static readonly string ConnectionString = "Host=localhost;Username=postgres;Password=yourpassword;Database=lottery";

    private static ITelegramBotClient botClient;

    static async Task Main()
    {
        botClient = new TelegramBotClient(BotToken);
        await InitializeDatabase();

        botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync);

        Console.WriteLine("🎟 Telegram Lottery Bot is running...");
        Console.ReadLine();
    }

    private static async Task InitializeDatabase()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS tickets (
                id SERIAL PRIMARY KEY,
                user_id BIGINT,
                username TEXT,
                ticket_number TEXT
            )");
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text) return;
        var message = update.Message;
        var chatId = message.Chat.Id;
        var text = message.Text?.ToLower();

        if (text == "/start")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("🎟 Buy Ticket"), new KeyboardButton("🏆 Draw Winner") }
            })
            {
                ResizeKeyboard = true
            };

            await bot.SendTextMessageAsync(chatId, "به قرعه کشی گنجینه خوش آمدید:", replyMarkup: keyboard);
        }
        else if (text == "🎟 buy ticket")
        {
            await BuyTicket(bot, message);
        }
        else if (text == "🏆 draw winner" && chatId == AdminChatId)
        {
            await DrawWinner(bot, chatId);
        }
    }

    private static async Task BuyTicket(ITelegramBotClient bot, Message message)
    {
        string ticketNumber = "LT-" + new Random().Next(100000, 999999);
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync("INSERT INTO tickets (user_id, username, ticket_number) VALUES (@UserId, @Username, @TicketNumber)",
            new { UserId = message.Chat.Id, Username = message.Chat.Username ?? "Unknown", TicketNumber = ticketNumber });

        await bot.SendTextMessageAsync(message.Chat.Id, $"✅ Ticket purchased! Your number: {ticketNumber}");
    }

    private static async Task DrawWinner(ITelegramBotClient bot, long chatId)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        var winner = await connection.QueryFirstOrDefaultAsync<(long UserId, string Username, string TicketNumber)>(
            "SELECT user_id, username, ticket_number FROM tickets ORDER BY RANDOM() LIMIT 1");

        if (winner != default)
        {
            await bot.SendTextMessageAsync(chatId, $"🏆 Winner: @{winner.Username} (Ticket: {winner.TicketNumber})");
            await connection.ExecuteAsync("DELETE FROM tickets"); // Clear tickets after draw
        }
        else
        {
            await bot.SendTextMessageAsync(chatId, "❌ No tickets available!");
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }
}
