using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;

public class UserStateService : IUserStateService
{
    private readonly string _connectionString;

    public UserStateService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public async Task SetStateAsync(long userId, string state)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        if (state == null)
        {
            await connection.ExecuteAsync(
                "DELETE FROM user_states WHERE user_id = @UserId",
                new { UserId = userId });
        }
        else
        {
            await connection.ExecuteAsync(
                "INSERT INTO user_states (user_id, state) VALUES (@UserId, @State) ON CONFLICT (user_id) DO UPDATE SET state = EXCLUDED.state",
                new { UserId = userId, State = state });
        }
    }

    public async Task<string> GetStateAsync(long userId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT state FROM user_states WHERE user_id = @UserId",
            new { UserId = userId });
    }
} 