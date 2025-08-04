using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("OrderItem")]
    public class OrderItem
    {
        [Key]
        public int OrderItemId { get; set; }

        [ForeignKey("OrderTable")]
        public int OrderId { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public int Quantity { get; set; }

        public decimal Price { get; set; }

        public OrderTable OrderTable { get; set; }
        public Product Product { get; set; }
    }
}
