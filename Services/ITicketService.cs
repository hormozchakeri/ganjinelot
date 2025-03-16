using System.Threading.Tasks;
using Telegram.Bot.Types;

public interface ITicketService
{
    Task<Ticket> CreateTicketAsync(long userId, string username, string imageFileId);
    Task<Ticket> ApproveTicketAsync(long userId);
    Task RejectTicketAsync(long userId);
    Task<Ticket> DrawWinnerAsync();
} 