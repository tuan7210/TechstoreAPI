using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TechstoreBackend.Models
{
    [Table("Product")]
    public class Product
    {
        [Key]
        [Column("product_id")]
        public int ProductId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("description")]
        public string Description { get; set; } = string.Empty;

        [Column("price")]
        public decimal Price { get; set; }

        [Column("original_price")]
        public decimal? OriginalPrice { get; set; }

        [Column("brand")]
        public string Brand { get; set; } = string.Empty;

        [Column("stock_quantity")]
        public int StockQuantity { get; set; }

        [Column("category_id")]
        [JsonPropertyName("category_id")]
        public int CategoryId { get; set; }

        [Column("image_url")]
        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; } = string.Empty;

        [Column("specifications")]
        public string Specifications { get; set; } = string.Empty; // JSON string

        [Column("rating")]
        public decimal Rating { get; set; }

        [Column("review_count")]
        public int ReviewCount { get; set; }

        [Column("is_new")]
        public bool IsNew { get; set; }

        [Column("is_best_seller")]
        public bool IsBestSeller { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonIgnore] // Không bắt buộc phải gửi Category trong request
        public Category? Category { get; set; }
    }
}
