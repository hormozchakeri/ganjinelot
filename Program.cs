using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static async Task Main()
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Initialize database
        await InitializeDatabase(configuration);

        // Get bot client and handler
        var botClient = serviceProvider.GetRequiredService<ITelegramBotClient>();
        var updateHandler = serviceProvider.GetRequiredService<TelegramUpdateHandler>();

        // Start bot
        botClient.StartReceiving(
            updateHandler.HandleUpdateAsync,
            updateHandler.HandleErrorAsync
        );

        Console.WriteLine("🎟 Telegram Lottery Bot is running...");
        Console.ReadLine();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Add bot client
        services.AddSingleton<ITelegramBotClient>(_ => 
            new TelegramBotClient(configuration["BotConfiguration:BotToken"]));

        // Add services
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<IUserStateService, UserStateService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<TelegramUpdateHandler>();

        // Create initial lottery if none exists
        using var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        var hasActiveLottery = connection.QueryFirstOrDefault<bool>(
            "SELECT EXISTS(SELECT 1 FROM lotteries WHERE status = 'active')");
        
        if (!hasActiveLottery)
        {
            connection.Execute(
                "INSERT INTO lotteries (status, ticket_price) VALUES ('active', @TicketPrice)",
                new { TicketPrice = 10000m });
        }
    }

    private static async Task InitializeDatabase(IConfiguration configuration)
    {
        using var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS tickets (
                id SERIAL PRIMARY KEY,
                user_id BIGINT,
                username TEXT,
                ticket_number TEXT,
                status TEXT DEFAULT 'pending',
                image_file_id TEXT,
                lottery_id BIGINT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS user_states (
                user_id BIGINT PRIMARY KEY,
                state TEXT
            );

            CREATE TABLE IF NOT EXISTS user_wallets (
                user_id BIGINT PRIMARY KEY,
                balance DECIMAL DEFAULT 0,
                last_updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS payment_images (
                id SERIAL PRIMARY KEY,
                user_id BIGINT,
                image_file_id TEXT,
                status TEXT DEFAULT 'pending',
                amount DECIMAL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS lotteries (
                id SERIAL PRIMARY KEY,
                start_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                end_date TIMESTAMP,
                winner_ticket_id BIGINT,
                status TEXT DEFAULT 'active',
                ticket_price DECIMAL DEFAULT 10000
            );
        ");
    }
}
