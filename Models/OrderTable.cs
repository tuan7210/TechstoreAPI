using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechstoreBackend.Models
{
    [Table("OrderTable")]
    public class OrderTable
    {
        [Key]
        public int OrderId { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public DateTime OrderDate { get; set; }

        public string Status { get; set; } // 'pending', 'shipped', 'completed', 'canceled'

        public decimal TotalAmount { get; set; }

        public string ShippingAddress { get; set; }

        public string PaymentStatus { get; set; } // 'unpaid', 'paid'

        public string PaymentMethod { get; set; }

        public User User { get; set; }
    }
}
