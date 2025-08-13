namespace TechstoreBackend.Models.DTOs
{
    public class OrderResponseDto
    {
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public List<OrderItemResponseDto> Items { get; set; } = new List<OrderItemResponseDto>();
        public string Email { get; set; }
        public string Phone { get; set; }
    }

    public class OrderItemResponseDto
    {
        public int OrderItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Subtotal { get; set; }
    }
}
