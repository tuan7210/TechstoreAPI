using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("Review")]
    public class Review
    {
        [Key]
        [Column("review_id")]
        public int ReviewId { get; set; }

        [Column("user_id")]
        [ForeignKey("User")]
        public int UserId { get; set; }

        [Column("product_id")]
        [ForeignKey("Product")]
        public int ProductId { get; set; }

        [Column("order_item_id")]
        [ForeignKey("OrderItem")]
        public int OrderItemId { get; set; }

        [Column("rating")]
        [Range(1, 5)]
        public int Rating { get; set; }

        [Column("comment")]
        public string Comment { get; set; } = string.Empty;

        [Column("is_verified")]
        public bool IsVerified { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public User? User { get; set; }
        public Product? Product { get; set; }
        public OrderItem? OrderItem { get; set; }
    }
}
