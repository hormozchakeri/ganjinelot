using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Dapper;
using Npgsql;

public class TelegramUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ITicketService _ticketService;
    private readonly IUserStateService _userStateService;
    private readonly IWalletService _walletService;
    private readonly string _connectionString;
    private readonly long _adminChatId;
    private readonly decimal _defaultPaymentAmount = 10000; // Default payment amount in Toman

    public TelegramUpdateHandler(
        ITelegramBotClient botClient,
        ITicketService ticketService,
        IUserStateService userStateService,
        IWalletService walletService,
        IConfiguration configuration)
    {
        _botClient = botClient;
        _ticketService = ticketService;
        _userStateService = userStateService;
        _walletService = walletService;
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _adminChatId = configuration.GetValue<long>("BotConfiguration:AdminChatId");
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;

            if (message.Type == MessageType.Text)
            {
                await HandleTextMessage(message);
            }
            else if (message.Type == MessageType.Photo)
            {
                await HandlePhotoMessage(message);
            }
        }
    }

    private async Task HandleTextMessage(Message message)
    {
        var chatId = message.Chat.Id;
        var text = message.Text?.ToLower();

        switch (text)
        {
            case "/start":
                await SendWelcomeMessage(chatId);
                break;
            case "شارژ کیف پول":
                await _userStateService.SetStateAsync(chatId, "waiting_for_payment_image");
                await _botClient.SendTextMessageAsync(chatId, 
                    $"📸 لطفاً تصویر رسید پرداخت {_defaultPaymentAmount} تومان را ارسال کنید.");
                break;
            case "خرید تیکت":
                await HandleTicketPurchase(chatId);
                break;
            case "موجودی کیف پول":
                await ShowWalletBalance(chatId);
                break;
            case "انجام قرعه کشی" when chatId == _adminChatId:
                await HandleDrawWinner(chatId);
                break;
            case "تاریخچه قرعه کشی" when chatId == _adminChatId:
                await ShowLotteryHistory(chatId);
                break;
            default:
                if (text?.StartsWith("/approve_payment") == true && chatId == _adminChatId)
                {
                    await HandleApprovePayment(text);
                }
                else if (text?.StartsWith("/reject_payment") == true && chatId == _adminChatId)
                {
                    await HandleRejectPayment(text);
                }
                break;
        }
    }

    private async Task HandlePhotoMessage(Message message)
    {
        var chatId = message.Chat.Id;
        var photo = message.Photo?.Length > 0 ? message.Photo[^1] : null;

        if (photo == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "❌ تصویر معتبر نیست. لطفاً دوباره ارسال کنید.");
            return;
        }

        var userState = await _userStateService.GetStateAsync(chatId);
        if (userState == "waiting_for_payment_image")
        {
            var paymentImage = await _walletService.CreatePaymentImageAsync(chatId, photo.FileId, _defaultPaymentAmount);
            await _botClient.SendTextMessageAsync(chatId, "✅ تصویر پرداخت ثبت شد. لطفاً منتظر تایید مدیر باشید.");
            await _botClient.SendTextMessageAsync(_adminChatId,
                $"🔔 کاربر @{message.Chat.Username} یک درخواست شارژ کیف پول به مبلغ {_defaultPaymentAmount} تومان ارسال کرده است.\n\n" +
                $"Approve: `/approve_payment {paymentImage.Id}`\nReject: `/reject_payment {paymentImage.Id}`",
                parseMode: ParseMode.Markdown);
            await _userStateService.SetStateAsync(chatId, null);
        }
    }

    private async Task SendWelcomeMessage(long chatId)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("شارژ کیف پول"), new KeyboardButton("موجودی کیف پول") },
            new[] { new KeyboardButton("خرید تیکت"), new KeyboardButton("انجام قرعه کشی") },
            chatId == _adminChatId ? new[] { new KeyboardButton("تاریخچه قرعه کشی") } : new KeyboardButton[] {}
        })
        {
            ResizeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(chatId, "به قرعه کشی گنجینه خوش آمدید:", replyMarkup: keyboard);
    }

    private async Task ShowWalletBalance(long chatId)
    {
        var balance = await _walletService.GetBalanceAsync(chatId);
        await _botClient.SendTextMessageAsync(chatId, $"💰 موجودی کیف پول شما: {balance:N0} تومان");
    }

    private async Task HandleTicketPurchase(long chatId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var activeLottery = await connection.QueryFirstOrDefaultAsync<Lottery>(
            "SELECT * FROM lotteries WHERE status = 'active' ORDER BY id DESC LIMIT 1");

        if (activeLottery == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "❌ در حال حاضر قرعه کشی فعالی وجود ندارد.");
            return;
        }

        var balance = await _walletService.GetBalanceAsync(chatId);
        if (balance < activeLottery.TicketPrice)
        {
            await _botClient.SendTextMessageAsync(chatId, 
                $"❌ موجودی کیف پول شما کافی نیست. موجودی فعلی: {balance:N0} تومان\n" +
                $"قیمت بلیت: {activeLottery.TicketPrice:N0} تومان");
            return;
        }

        if (await _walletService.DeductBalanceAsync(chatId, activeLottery.TicketPrice))
        {
            var ticket = await _ticketService.CreateTicketAsync(chatId, null, null);
            await _botClient.SendTextMessageAsync(chatId, 
                $"✅ بلیت شما با موفقیت خریداری شد.\n" +
                $"شماره بلیت: {ticket.TicketNumber}\n" +
                $"موجودی باقیمانده: {(balance - activeLottery.TicketPrice):N0} تومان");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "❌ خطا در خرید بلیت. لطفاً دوباره تلاش کنید.");
        }
    }

    private async Task HandleApprovePayment(string text)
    {
        var parts = text.Split(" ");
        if (parts.Length == 2 && int.TryParse(parts[1], out int imageId))
        {
            try
            {
                var paymentImage = await _walletService.ApprovePaymentImageAsync(imageId);
                var newBalance = await _walletService.GetBalanceAsync(paymentImage.UserId);
                
                await _botClient.SendTextMessageAsync(paymentImage.UserId,
                    $"✅ پرداخت شما تایید شد.\nموجودی فعلی: {newBalance:N0} تومان");
                await _botClient.SendTextMessageAsync(_adminChatId,
                    $"✅ پرداخت کاربر {paymentImage.UserId} به مبلغ {paymentImage.Amount:N0} تومان تایید شد.");
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync(_adminChatId, $"❌ خطا در تایید پرداخت: {ex.Message}");
            }
        }
    }

    private async Task HandleRejectPayment(string text)
    {
        var parts = text.Split(" ");
        if (parts.Length == 2 && int.TryParse(parts[1], out int imageId))
        {
            await _walletService.RejectPaymentImageAsync(imageId);
            using var connection = new NpgsqlConnection(_connectionString);
            var paymentImage = await connection.QueryFirstOrDefaultAsync<PaymentImage>(
                "SELECT * FROM payment_images WHERE id = @Id",
                new { Id = imageId });

            if (paymentImage != null)
            {
                await _botClient.SendTextMessageAsync(paymentImage.UserId,
                    "❌ متاسفانه پرداخت شما تایید نشد. لطفاً دوباره تلاش کنید.");
                await _botClient.SendTextMessageAsync(_adminChatId,
                    $"❌ پرداخت کاربر {paymentImage.UserId} رد شد.");
            }
        }
    }

    private async Task HandleDrawWinner(long chatId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var activeLottery = await connection.QueryFirstOrDefaultAsync<Lottery>(
            "SELECT * FROM lotteries WHERE status = 'active' ORDER BY id DESC LIMIT 1");

        if (activeLottery == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "❌ قرعه کشی فعالی وجود ندارد.");
            return;
        }

        var winner = await _ticketService.DrawWinnerAsync();
        if (winner != null)
        {
            await connection.ExecuteAsync(@"
                UPDATE lotteries 
                SET status = 'completed', 
                    end_date = CURRENT_TIMESTAMP,
                    winner_ticket_id = @TicketId 
                WHERE id = @LotteryId",
                new { TicketId = winner.Id, LotteryId = activeLottery.Id });

            // Create new lottery
            await connection.ExecuteAsync(
                "INSERT INTO lotteries (status, ticket_price) VALUES ('active', @TicketPrice)",
                new { TicketPrice = activeLottery.TicketPrice });

            await _botClient.SendTextMessageAsync(chatId,
                $"🏆 برنده قرعه کشی: @{winner.Username} (شماره بلیت: {winner.TicketNumber})");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "❌ هیچ بلیتی در قرعه کشی شرکت نکرده است!");
        }
    }

    private async Task ShowLotteryHistory(long chatId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var lotteries = await connection.QueryAsync<Lottery>(@"
            SELECT l.*, t.username as winner_username, t.ticket_number as winner_ticket_number
            FROM lotteries l
            LEFT JOIN tickets t ON l.winner_ticket_id = t.id
            WHERE l.status = 'completed'
            ORDER BY l.end_date DESC
            LIMIT 10");

        var message = "📜 تاریخچه قرعه کشی‌های اخیر:\n\n";
        foreach (var lottery in lotteries)
        {
            message += $"🎯 تاریخ: {lottery.EndDate:yyyy/MM/dd HH:mm}\n";
            message += $"👤 برنده: @{lottery.WinnerUsername}\n";
            message += $"🎫 شماره بلیت: {lottery.WinnerTicketNumber}\n\n";
        }

        await _botClient.SendTextMessageAsync(chatId, message);
    }

    public Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"❌ Error: {exception.Message}");
        return Task.CompletedTask;
    }
} 