using System.ComponentModel.DataAnnotations;

namespace Streetpay.API.Models.DTOs
{
    public class OnlineTransactionDto
    {
        [Required]
        public int SenderId { get; set; }

        [Required]
        public int ReceiverId { get; set; }

        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Nonce { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Signature { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
