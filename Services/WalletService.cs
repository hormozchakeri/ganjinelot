using System;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;

public class WalletService : IWalletService
{
    private readonly string _connectionString;

    public WalletService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public async Task<decimal> GetBalanceAsync(long userId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var balance = await connection.QueryFirstOrDefaultAsync<decimal?>(
            "SELECT balance FROM user_wallets WHERE user_id = @UserId",
            new { UserId = userId });
        
        return balance ?? 0;
    }

    public async Task<decimal> AddBalanceAsync(long userId, decimal amount)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstAsync<decimal>(@"
            INSERT INTO user_wallets (user_id, balance) 
            VALUES (@UserId, @Amount)
            ON CONFLICT (user_id) DO UPDATE 
            SET balance = user_wallets.balance + @Amount,
                last_updated = CURRENT_TIMESTAMP
            RETURNING balance",
            new { UserId = userId, Amount = amount });
    }

    public async Task<bool> DeductBalanceAsync(long userId, decimal amount)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.ExecuteAsync(@"
            UPDATE user_wallets 
            SET balance = balance - @Amount,
                last_updated = CURRENT_TIMESTAMP
            WHERE user_id = @UserId AND balance >= @Amount",
            new { UserId = userId, Amount = amount });
        
        return result > 0;
    }

    public async Task<PaymentImage> CreatePaymentImageAsync(long userId, string imageFileId, decimal amount)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QueryFirstAsync<int>(@"
            INSERT INTO payment_images (user_id, image_file_id, amount)
            VALUES (@UserId, @ImageFileId, @Amount)
            RETURNING id",
            new { UserId = userId, ImageFileId = imageFileId, Amount = amount });

        return await connection.QueryFirstAsync<PaymentImage>(
            "SELECT * FROM payment_images WHERE id = @Id",
            new { Id = id });
    }

    public async Task<PaymentImage> ApprovePaymentImageAsync(int imageId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        using var transaction = connection.BeginTransaction();

        try
        {
            var paymentImage = await connection.QueryFirstAsync<PaymentImage>(
                "SELECT * FROM payment_images WHERE id = @Id FOR UPDATE",
                new { Id = imageId },
                transaction);

            if (paymentImage.Status != "pending")
            {
                throw new InvalidOperationException("Payment image is not in pending status");
            }

            await connection.ExecuteAsync(
                "UPDATE payment_images SET status = 'approved' WHERE id = @Id",
                new { Id = imageId },
                transaction);

            await AddBalanceAsync(paymentImage.UserId, paymentImage.Amount);

            transaction.Commit();
            return paymentImage;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task RejectPaymentImageAsync(int imageId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "UPDATE payment_images SET status = 'rejected' WHERE id = @Id",
            new { Id = imageId });
    }
} 