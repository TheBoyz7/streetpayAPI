public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SenderPhone { get; set; } = string.Empty;
    public string ReceiverPhone { get; set; } = string.Empty;
    public double Amount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "pending"; // pending, synced, failed
}
