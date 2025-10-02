using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Streetpay.API.Models
{
    public class Transaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(64)]
        public string TransactionId { get; set; } = string.Empty;

        public int? SenderId { get; set; }
        public int? ReceiverId { get; set; }

        [Required]
        [Phone]
        public string SenderPhone { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required]
        [Range(0.01, 79228162514264337593543950335.0)]
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal? SenderNewBalance { get; set; } // New field for sender's balance after transaction

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal? ReceiverNewBalance { get; set; } // New field for receiver's balance after transaction

        [MaxLength(3)]
        public string Currency { get; set; } = "NGN";

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool Used { get; set; } = false;

        [MaxLength(20)]
        public string Type { get; set; } = "offline";

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "pending";

        public bool IsSynced { get; set; } = false;

        [MaxLength(256)]
        public string Signature { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Nonce { get; set; } = string.Empty;

        public virtual User Sender { get; set; } 

        public virtual User Receiver { get; set; } 
    }
}