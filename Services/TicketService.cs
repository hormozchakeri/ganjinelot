using System;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;

public class TicketService : ITicketService
{
    private readonly string _connectionString;

    public TicketService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public async Task<Ticket> CreateTicketAsync(long userId, string username, string imageFileId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var ticket = new Ticket
        {
            UserId = userId,
            Username = username ?? "Unknown",
            ImageFileId = imageFileId,
            Status = "pending"
        };

        var id = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO tickets (user_id, username, image_file_id, status) VALUES (@UserId, @Username, @ImageFileId, @Status) RETURNING id",
            ticket);
        
        ticket.Id = id;
        return ticket;
    }

    public async Task<Ticket> ApproveTicketAsync(long userId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var ticketNumber = "LT-" + new Random().Next(100000, 999999);
        
        var ticket = await connection.QueryFirstOrDefaultAsync<Ticket>(
            "UPDATE tickets SET ticket_number = @TicketNumber, status = 'approved' WHERE user_id = @UserId AND status = 'pending' RETURNING *",
            new { UserId = userId, TicketNumber = ticketNumber });

        return ticket;
    }

    public async Task RejectTicketAsync(long userId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "DELETE FROM tickets WHERE user_id = @UserId AND status = 'pending'",
            new { UserId = userId });
    }

    public async Task<Ticket> DrawWinnerAsync()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var winner = await connection.QueryFirstOrDefaultAsync<Ticket>(
            "SELECT * FROM tickets WHERE status = 'approved' ORDER BY RANDOM() LIMIT 1");

        if (winner != null)
        {
            await connection.ExecuteAsync("DELETE FROM tickets WHERE status = 'approved'");
        }

        return winner;
    }
} 