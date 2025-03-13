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
    private static readonly long AdminChatId = 1055113814;
    private static readonly string ConnectionString = "Host=localhost;Username=botuser;Password=21151220;Database=ganjine";

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
                ticket_number TEXT,
                status TEXT DEFAULT 'pending',
                image_file_id TEXT
            );

            CREATE TABLE IF NOT EXISTS user_states (
                user_id BIGINT PRIMARY KEY,
                state TEXT
            );
        ");
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;

            if (message.Type == MessageType.Text)
            {
                await HandleTextMessage(bot, message);
            }
            else if (message.Type == MessageType.Photo)
            {
                await HandlePhotoMessage(bot, message);
            }
        }
    }

    private static async Task HandleTextMessage(ITelegramBotClient bot, Message message)
    {
        var chatId = message.Chat.Id;
        var text = message.Text?.ToLower();

        if (text == "/start")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("خرید تیکت"), new KeyboardButton("انجام قرعه کشی") }
            })
            {
                ResizeKeyboard = true
            };

            await bot.SendTextMessageAsync(chatId, "به قرعه کشی گنجینه خوش آمدید:", replyMarkup: keyboard);
        }
        else if (text == "خرید تیکت")
        {
            await SetUserState(chatId, "waiting_for_image");
            await bot.SendTextMessageAsync(chatId, "📸 لطفاً تصویر رسید پرداخت خود ارسال کنید.");
        }
        else if (text == "انجام قرعه کشی" && chatId == AdminChatId)
        {
            await DrawWinner(bot, chatId);
        }
        else if (text.StartsWith("/approve") && chatId == AdminChatId)
        {
            var parts = text.Split(" ");
            if (parts.Length == 2 && long.TryParse(parts[1], out long userId))
            {
                await ApproveTicket(bot, userId);
            }
        }
        else if (text.StartsWith("/reject") && chatId == AdminChatId)
        {
            var parts = text.Split(" ");
            if (parts.Length == 2 && long.TryParse(parts[1], out long userId))
            {
                await RejectTicket(bot, userId);
            }
        }
    }

    private static async Task HandlePhotoMessage(ITelegramBotClient bot, Message message)
    {
        var chatId = message.Chat.Id;
        var photo = message.Photo?.Length > 0 ? message.Photo[^1] : null;

        if (photo == null)
        {
            await bot.SendTextMessageAsync(chatId, "❌ رسید واریز معتبر نیست. لطفاً دوباره ارسال کنید.");
            return;
        }

        var userState = await GetUserState(chatId);
        if (userState == "waiting_for_image")
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.ExecuteAsync("INSERT INTO tickets (user_id, username, image_file_id, status) VALUES (@UserId, @Username, @ImageFileId, 'pending')",
                new { UserId = chatId, Username = message.Chat.Username ?? "Unknown", ImageFileId = photo.FileId });

            await bot.SendTextMessageAsync(chatId, "✅ عکس ثبت شد. لطفاً منتظر تایید مدیر باشید.");
            await bot.SendTextMessageAsync(AdminChatId, $"🔔 کاربر @{message.Chat.Username} یک درخواست جدید ارسال کرده است.\n\nApprove: `/approve {chatId}`\nReject: `/reject {chatId}`",
                parseMode: ParseMode.Markdown);
            await SetUserState(chatId, null);
        }
    }

    private static async Task ApproveTicket(ITelegramBotClient bot, long userId)
    {
        string ticketNumber = "LT-" + new Random().Next(100000, 999999);
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync("UPDATE tickets SET ticket_number = @TicketNumber, status = 'approved' WHERE user_id = @UserId",
            new { UserId = userId, TicketNumber = ticketNumber });

        await bot.SendTextMessageAsync(userId, $"✅ تبریک! بلیت شما تایید شد. شماره بلیت شما: {ticketNumber}");
        await bot.SendTextMessageAsync(AdminChatId, $"✅ کاربر {userId} تایید شد و بلیت {ticketNumber} به او ارسال شد.");
    }

    private static async Task RejectTicket(ITelegramBotClient bot, long userId)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync("DELETE FROM tickets WHERE user_id = @UserId AND status = 'pending'", new { UserId = userId });

        await bot.SendTextMessageAsync(userId, "❌ متاسفیم، رسید شما تایید نشد. لطفاً یک عکس جدید ارسال کنید.");
        await bot.SendTextMessageAsync(AdminChatId, $"❌ کاربر {userId} رد شد.");
    }

    private static async Task DrawWinner(ITelegramBotClient bot, long chatId)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        var winner = await connection.QueryFirstOrDefaultAsync<(long UserId, string Username, string TicketNumber)>(
            "SELECT user_id, username, ticket_number FROM tickets WHERE status = 'approved' ORDER BY RANDOM() LIMIT 1");

        if (winner != default)
        {
            await bot.SendTextMessageAsync(chatId, $"🏆 برنده قرعه کشی: @{winner.Username} (شماره بلیت: {winner.TicketNumber})");
            await connection.ExecuteAsync("DELETE FROM tickets WHERE status = 'approved'");
        }
        else
        {
            await bot.SendTextMessageAsync(chatId, "❌ هیچ بلیتی تایید نشده است!");
        }
    }

    private static async Task SetUserState(long userId, string state)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        if (state == null)
        {
            await connection.ExecuteAsync("DELETE FROM user_states WHERE user_id = @UserId", new { UserId = userId });
        }
        else
        {
            await connection.ExecuteAsync("INSERT INTO user_states (user_id, state) VALUES (@UserId, @State) ON CONFLICT (user_id) DO UPDATE SET state = EXCLUDED.state",
                new { UserId = userId, State = state });
        }
    }

    private static async Task<string> GetUserState(long userId)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.QueryFirstOrDefaultAsync<string>("SELECT state FROM user_states WHERE user_id = @UserId", new { UserId = userId });
    }
    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"❌ Error: {exception.Message}");
        return Task.CompletedTask;
    }
}
