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
        public string Name { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("price")]
        public decimal Price { get; set; }

        [Column("stock_quantity")]
        public int StockQuantity { get; set; }

        [Column("category_id")]
        [JsonPropertyName("category_id")]
        public int CategoryId { get; set; }

        [Column("image_url")]
        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonIgnore] // Không bắt buộc phải gửi Category trong request
        public Category Category { get; set; }
    }
}
