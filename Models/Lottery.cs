using System;

public class Lottery
{
    public int Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public long? WinnerTicketId { get; set; }
    public string Status { get; set; }
    public decimal TicketPrice { get; set; }
} 