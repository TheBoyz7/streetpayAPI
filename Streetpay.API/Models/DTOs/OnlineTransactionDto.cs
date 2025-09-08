using System.ComponentModel.DataAnnotations;

namespace Streetpay.API.Models.DTOs
{
    /// <summary>
    /// Data transfer object for online transactions.
    /// </summary>
    public class OnlineTransactionDto
    {
        /// <summary>
        /// ID of the sender.
        /// </summary>
        [Required]
        public int SenderId { get; set; }

        /// <summary>
        /// ID of the receiver.
        /// </summary>
        [Required]
        public int ReceiverId { get; set; }

        /// <summary>
        /// Transaction amount (in NGN).
        /// </summary>
        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Hashed transaction ID (SHA-256).
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;
    }
}