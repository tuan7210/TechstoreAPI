using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("Cart")]
    public class Cart
    {
        [Key]
        [Column("cart_id")]
        public int CartId { get; set; }

        [Column("user_id")]
        [ForeignKey("User")]
        public int UserId { get; set; }

        public User? User { get; set; }
    }
}
