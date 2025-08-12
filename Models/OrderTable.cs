using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("OrderTable")]
    public class OrderTable
    {
        [Key]
        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("user_id")]
        [ForeignKey("User")]
        public int UserId { get; set; }

        [Column("order_date")]
        public DateTime OrderDate { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty; // 'pending', 'shipped', 'completed', 'canceled'

        [Column("total_amount")]
        public decimal TotalAmount { get; set; }

        [Column("shipping_address")]
        public string ShippingAddress { get; set; } = string.Empty;

        [Column("payment_status")]
        public string PaymentStatus { get; set; } = string.Empty; // 'unpaid', 'paid'

        [Column("payment_method")]
        public string PaymentMethod { get; set; } = string.Empty;

        public User? User { get; set; }
    }
}
