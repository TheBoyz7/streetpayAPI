using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Streetpay.API.Models
{
    /// <summary>
    /// Represents a financial transaction between two users.
    /// </summary>
    public class Transaction
    {
        /// <summary>
        /// Unique identifier for the transaction (auto-generated GUID).
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Hashed transaction ID provided by the client (SHA-256).
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;

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
        /// Transaction amount (in NGN, up to 2 decimal places).
        /// </summary>
        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Timestamp of the transaction (UTC).
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Transaction status (e.g., pending, sent, synced, sent-offline, failed).
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Indicates whether the transaction has been synchronized with the server.
        /// </summary>
        public bool IsSynced { get; set; } = false;
    }
}