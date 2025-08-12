namespace TechstoreBackend.Models.DTOs
{
    public class ProductResponseDto
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string Brand { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public object? Specifications { get; set; } // Parsed JSON object
        public decimal Rating { get; set; }
        public int ReviewCount { get; set; }
        public bool IsNew { get; set; }
        public bool IsBestSeller { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        
        // Computed fields
        public decimal? DiscountPercentage => OriginalPrice.HasValue && OriginalPrice > 0 
            ? Math.Round((OriginalPrice.Value - Price) / OriginalPrice.Value * 100, 1) 
            : null;
        
        public bool IsOnSale => OriginalPrice.HasValue && OriginalPrice > Price;
        public bool IsInStock => StockQuantity > 0;
    }
}
