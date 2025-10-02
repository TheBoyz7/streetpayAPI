using System;
using System.ComponentModel.DataAnnotations;

namespace Streetpay.API.Models.DTOs
{
    public class OfflineTransactionDto
    {
        [Required]
        [Phone]
        public string SenderPhone { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        public decimal Amount { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;

        public decimal? SenderNewBalance { get; set; }

        public decimal? ReceiverNewBalance { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "NGN";

        [Required]
        [MaxLength(64)]
        public string Nonce { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Signature { get; set; } = string.Empty;
    }
}