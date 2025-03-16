using System.Threading.Tasks;

public interface IWalletService
{
    Task<decimal> GetBalanceAsync(long userId);
    Task<decimal> AddBalanceAsync(long userId, decimal amount);
    Task<bool> DeductBalanceAsync(long userId, decimal amount);
    Task<PaymentImage> CreatePaymentImageAsync(long userId, string imageFileId, decimal amount);
    Task<PaymentImage> ApprovePaymentImageAsync(int imageId);
    Task RejectPaymentImageAsync(int imageId);
} 