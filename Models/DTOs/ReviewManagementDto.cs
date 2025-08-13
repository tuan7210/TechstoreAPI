using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class ReviewManagementDto
    {
        public int ReviewId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductBrand { get; set; } = string.Empty;
        public string ProductImageUrl { get; set; } = string.Empty;
        public int OrderItemId { get; set; }
        public int OrderId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string OrderStatus { get; set; } = string.Empty;
    }

    public class ReviewFilterDto
    {
        public int? ProductId { get; set; }
        public int? UserId { get; set; }
        public int? Rating { get; set; }
        public bool? IsVerified { get; set; }
        public string? ProductName { get; set; }
        public string? UserName { get; set; }
        public string? Brand { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SearchKeyword { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortOrder { get; set; } = "desc";
    }

    public class ReviewActionDto
    {
        [Required(ErrorMessage = "ID đánh giá là bắt buộc")]
        public int ReviewId { get; set; }
        
        public string? AdminNote { get; set; }
    }

    public class BulkReviewActionDto
    {
        [Required(ErrorMessage = "Danh sách ID đánh giá không được để trống")]
        public List<int> ReviewIds { get; set; } = new List<int>();
        
        public string? AdminNote { get; set; }
    }

    public class ReviewStatisticsDto
    {
        public int TotalReviews { get; set; }
        public int VerifiedReviews { get; set; }
        public int UnverifiedReviews { get; set; }
        public int PendingReviews { get; set; }
        public double AverageRating { get; set; }
        public Dictionary<int, int> RatingDistribution { get; set; } = new Dictionary<int, int>();
        public int ReviewsThisMonth { get; set; }
        public int ReviewsLastMonth { get; set; }
        public List<TopReviewedProductDto> TopReviewedProducts { get; set; } = new List<TopReviewedProductDto>();
        public List<MostActiveReviewerDto> MostActiveReviewers { get; set; } = new List<MostActiveReviewerDto>();
    }

    public class TopReviewedProductDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public int ReviewCount { get; set; }
        public double AverageRating { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class MostActiveReviewerDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int ReviewCount { get; set; }
        public double AverageRating { get; set; }
        public int VerifiedReviewCount { get; set; }
    }

    public class ProductReviewSummaryDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int TotalReviews { get; set; }
        public int VerifiedReviews { get; set; }
        public double AverageRating { get; set; }
        public Dictionary<int, int> RatingDistribution { get; set; } = new Dictionary<int, int>();
        public List<ReviewManagementDto> RecentReviews { get; set; } = new List<ReviewManagementDto>();
    }
}
