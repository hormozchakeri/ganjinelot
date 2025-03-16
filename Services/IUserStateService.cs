using System.Threading.Tasks;

public interface IUserStateService
{
    Task SetStateAsync(long userId, string state);
    Task<string> GetStateAsync(long userId);
} 