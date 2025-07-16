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
        public string Name { get; set; }

        [Required]
        public string Phone { get; set; }

        [Required]
        public string Pin { get; set; }  // TODO: Hash in production

        [Required]
        [Column(TypeName = "REAL")]
        public double MainBalance { get; set; }

        [Required]
        [Column(TypeName = "REAL")]
        public double SavingsBalance { get; set; } 
    }
}
