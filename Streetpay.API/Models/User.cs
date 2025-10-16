using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Streetpay.API.Models
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string Phone { get; set; } = string.Empty;

        [Required]
        public string Pin { get; set; } = string.Empty; // TODO: Hash in production

        [Required]
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal MainBalance { get; set; }

        [Required]
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal SavingsBalance { get; set; }

        [Required]
        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal OfflineBalance { get; set; } = 0;
}
    
}