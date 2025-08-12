using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("CartItem")]
    public class CartItem
    {
        [Key]
        [Column("cart_item_id")]
        public int CartItemId { get; set; }

        [Column("cart_id")]
        [ForeignKey("Cart")]
        public int CartId { get; set; }

        [Column("product_id")]
        [ForeignKey("Product")]
        public int ProductId { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        public Cart? Cart { get; set; }
        public Product? Product { get; set; }
    }
}
