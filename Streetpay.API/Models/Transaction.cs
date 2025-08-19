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
        [Phone]
        public string SenderPhone { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required]
        [Range(0.01, 79228162514264337593543950335.0)] // Max value for decimal
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(10)]
        public string Status { get; set; } = "pending"; // pending, synced, failed

        public bool IsSynced { get; set; } = false;
    }
}