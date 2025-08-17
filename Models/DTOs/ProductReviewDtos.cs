using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    // DTO cho query đánh giá sản phẩm (khách hàng)
    public class ReviewQueryDto
    {
        public int? Rating { get; set; } // Filter theo rating
        public string SortBy { get; set; } = "createdat"; // createdat, rating
        public string SortOrder { get; set; } = "desc"; // asc, desc
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    // DTO cho đánh giá sản phẩm hiển thị cho khách hàng
    public class ProductReviewDto
    {
        public int ReviewId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsVerified { get; set; } // Thêm thuộc tính này để fix lỗi đỏ
    }

    // DTO cho tạo đánh giá mới
    public class CreateReviewDto
    {
        [Required(ErrorMessage = "ID sản phẩm là bắt buộc")]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "ID order item là bắt buộc")]
        public int OrderItemId { get; set; }

        [Required(ErrorMessage = "Rating là bắt buộc")]
        [Range(1, 5, ErrorMessage = "Rating phải từ 1 đến 5")]
        public int Rating { get; set; }

        [MaxLength(1000, ErrorMessage = "Comment không được quá 1000 ký tự")]
        public string? Comment { get; set; }
    }
}
