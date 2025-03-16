using System;

public class PaymentImage
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public string ImageFileId { get; set; }
    public string Status { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
} 