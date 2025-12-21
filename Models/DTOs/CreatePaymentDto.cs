namespace TechstoreBackend.Models.DTOs
{
    public class CreatePaymentDto
    {
        public int ProductId { get; set; } // Ví dụ: ID sản phẩm
        public string ProductName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Price { get; set; } // Giá tiền
    }
}