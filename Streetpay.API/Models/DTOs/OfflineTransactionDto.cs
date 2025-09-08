using System;
using System.ComponentModel.DataAnnotations;

namespace Streetpay.API.Models.DTOs
{
    /// <summary>
    /// Data transfer object for offline transactions.
    /// </summary>
    public class OfflineTransactionDto
    {
        /// <summary>
        /// Phone number of the sender.
        /// </summary>
        [Required]
        [Phone]
        public string SenderPhone { get; set; } = string.Empty;

        /// <summary>
        /// Phone number of the receiver.
        /// </summary>
        [Required]
        [Phone]
        public string ReceiverPhone { get; set; } = string.Empty;

        /// <summary>
        /// Transaction amount (in NGN).
        /// </summary>
        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Timestamp of the transaction.
        /// </summary>
        [Required]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Hashed transaction ID (SHA-256).
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;
    }
}