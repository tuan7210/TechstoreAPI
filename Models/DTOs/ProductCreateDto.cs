using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class ProductCreateDto
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(255, ErrorMessage = "Tên sản phẩm không được vượt quá 255 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mô tả sản phẩm là bắt buộc")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Giá sản phẩm là bắt buộc")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
        public decimal Price { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Giá gốc phải lớn hơn 0")]
        public decimal? OriginalPrice { get; set; }

        [Required(ErrorMessage = "Thương hiệu là bắt buộc")]
        [StringLength(255, ErrorMessage = "Thương hiệu không được vượt quá 255 ký tự")]
        public string Brand { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số lượng tồn kho là bắt buộc")]
        [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho phải >= 0")]
        public int StockQuantity { get; set; }

        [Required(ErrorMessage = "Danh mục sản phẩm là bắt buộc")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Hình ảnh sản phẩm là bắt buộc")]
        [Url(ErrorMessage = "URL hình ảnh không hợp lệ")]
        public string ImageUrl { get; set; } = string.Empty;

        public string? Specifications { get; set; } // JSON string

        public bool IsNew { get; set; } = false;
        public bool IsBestSeller { get; set; } = false;
    }

    public class ProductUpdateDto : ProductCreateDto
    {
        public int ProductId { get; set; }
    }
}
