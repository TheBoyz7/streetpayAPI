namespace Streetpay.API.Models.DTOs
{
    public class OnlineTransactionDto
    {
        public int SenderId { get; set; }
        public int ReceiverId { get; set; }
        public decimal Amount { get; set; }
    }
}