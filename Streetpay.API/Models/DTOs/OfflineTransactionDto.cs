namespace Streetpay.API.Models.DTOs
{
    public class OfflineTransactionDto
    {
        public string SenderPhone { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
    }
}