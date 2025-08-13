using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductReviewController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProductReviewController> _logger;

        public ProductReviewController(AppDbContext context, ILogger<ProductReviewController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/ProductReview/product/{productId} - Lấy đánh giá của sản phẩm (Public)
        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetProductReviews(int productId, [FromQuery] ReviewQueryDto query)
        {
            try
            {
                // Kiểm tra sản phẩm có tồn tại không
                var productExists = await _context.Products.AnyAsync(p => p.ProductId == productId);
                if (!productExists)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy sản phẩm"
                    });
                }

                var reviewQuery = _context.Reviews
                    .Include(r => r.User)
                    .Include(r => r.OrderItem)
                    .ThenInclude(oi => oi.OrderTable)
                    .Where(r => r.ProductId == productId && r.IsVerified) // Chỉ lấy đánh giá đã duyệt
                    .AsQueryable();

                // Apply filters
                if (query.Rating.HasValue)
                    reviewQuery = reviewQuery.Where(r => r.Rating == query.Rating.Value);

                // Đếm tổng số đánh giá
                var totalCount = await reviewQuery.CountAsync();

                // Sắp xếp
                reviewQuery = query.SortBy.ToLower() switch
                {
                    "rating" => query.SortOrder == "desc" ? reviewQuery.OrderByDescending(r => r.Rating) : reviewQuery.OrderBy(r => r.Rating),
                    _ => query.SortOrder == "desc" ? reviewQuery.OrderByDescending(r => r.CreatedAt) : reviewQuery.OrderBy(r => r.CreatedAt)
                };

                // Phân trang
                var reviews = await reviewQuery
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(r => new ProductReviewDto
                    {
                        ReviewId = r.ReviewId,
                        UserName = r.User.Name,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        CreatedAt = r.CreatedAt
                    })
                    .ToListAsync();

                // Thống kê rating
                var ratingStats = await _context.Reviews
                    .Where(r => r.ProductId == productId && r.IsVerified)
                    .GroupBy(r => r.Rating)
                    .Select(g => new { Rating = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Rating, x => x.Count);

                var averageRating = await _context.Reviews
                    .Where(r => r.ProductId == productId && r.IsVerified)
                    .AverageAsync(r => (double?)r.Rating) ?? 0.0;

                return Ok(new
                {
                    success = true,
                    message = "Lấy đánh giá sản phẩm thành công",
                    data = new
                    {
                        reviews = reviews,
                        statistics = new
                        {
                            totalReviews = totalCount,
                            averageRating = Math.Round(averageRating, 1),
                            ratingDistribution = ratingStats
                        },
                        pagination = new
                        {
                            page = query.Page,
                            pageSize = query.PageSize,
                            totalCount = totalCount,
                            totalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy đánh giá sản phẩm {ProductId}", productId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy đánh giá sản phẩm",
                    error = ex.Message
                });
            }
        }

        // POST: api/ProductReview - Tạo đánh giá mới (Yêu cầu đăng nhập)
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateReview([FromBody] CreateReviewDto reviewDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Dữ liệu không hợp lệ",
                        errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
                    });
                }

                // Lấy UserId từ JWT token
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (!int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Token không hợp lệ"
                    });
                }

                // Kiểm tra OrderItem có tồn tại và thuộc về user này không
                var orderItem = await _context.OrderItems
                    .Include(oi => oi.OrderTable)
                    .FirstOrDefaultAsync(oi => oi.OrderItemId == reviewDto.OrderItemId &&
                                              oi.OrderTable.UserId == userId &&
                                              oi.ProductId == reviewDto.ProductId);

                if (orderItem == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Không tìm thấy sản phẩm trong đơn hàng của bạn"
                    });
                }

                // Kiểm tra đơn hàng đã giao thành công chưa
                if (orderItem.OrderTable.Status != "completed")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Chỉ có thể đánh giá sản phẩm đã được giao thành công",
                        currentOrderStatus = orderItem.OrderTable.Status
                    });
                }

                // Kiểm tra đã đánh giá chưa
                var existingReview = await _context.Reviews
                    .AnyAsync(r => r.OrderItemId == reviewDto.OrderItemId && r.UserId == userId);

                if (existingReview)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Bạn đã đánh giá sản phẩm này rồi"
                    });
                }

                // Tạo đánh giá mới
                var review = new Review
                {
                    UserId = userId,
                    ProductId = reviewDto.ProductId,
                    OrderItemId = reviewDto.OrderItemId,
                    Rating = reviewDto.Rating,
                    Comment = reviewDto.Comment ?? string.Empty,
                    IsVerified = false, // Mặc định chưa duyệt
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Reviews.Add(review);
                await _context.SaveChangesAsync();

                // Cập nhật rating và review count của sản phẩm
                await UpdateProductRatingAsync(reviewDto.ProductId);

                _logger.LogInformation("User {UserId} đã tạo đánh giá cho sản phẩm {ProductId}", userId, reviewDto.ProductId);

                return Ok(new
                {
                    success = true,
                    message = "Tạo đánh giá thành công. Đánh giá sẽ được hiển thị sau khi admin duyệt.",
                    data = new
                    {
                        reviewId = review.ReviewId,
                        rating = review.Rating,
                        comment = review.Comment,
                        isVerified = review.IsVerified,
                        createdAt = review.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo đánh giá");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi tạo đánh giá",
                    error = ex.Message
                });
            }
        }

        // Helper method để cập nhật rating và review count của sản phẩm
        private async Task UpdateProductRatingAsync(int productId)
        {
            var verifiedReviews = await _context.Reviews
                .Where(r => r.ProductId == productId && r.IsVerified)
                .ToListAsync();

            var product = await _context.Products.FindAsync(productId);
            if (product != null)
            {
                if (verifiedReviews.Any())
                {
                    product.Rating = (decimal)verifiedReviews.Average(r => r.Rating);
                    product.ReviewCount = verifiedReviews.Count;
                }
                else
                {
                    product.Rating = 0;
                    product.ReviewCount = 0;
                }

                product.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}
