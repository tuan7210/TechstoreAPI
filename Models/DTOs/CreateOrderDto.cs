using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class CreateOrderDto
    {
        // UserId không còn là required vì sẽ lấy từ token JWT
        public int UserId { get; set; } = 0;

        [Required]
        public string ShippingAddress { get; set; } = string.Empty;

        [Required]
        public string PaymentMethod { get; set; } = string.Empty; // 'cash_on_delivery', 'credit_card', etc.

        [Required]
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }

    public class OrderItemDto
    {
        [Required]
        public int ProductId { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}
