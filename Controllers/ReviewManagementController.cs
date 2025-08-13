using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class ReviewManagementController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ReviewManagementController> _logger;

        public ReviewManagementController(AppDbContext context, ILogger<ReviewManagementController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/admin/ReviewManagement - Lấy danh sách tất cả đánh giá với filter
        [HttpGet]
        public async Task<IActionResult> GetAllReviews([FromQuery] ReviewFilterDto filter)
        {
            try
            {
                var query = _context.Reviews
                    .Include(r => r.User)
                    .Include(r => r.Product)
                    .ThenInclude(p => p.Category)
                    .Include(r => r.OrderItem)
                    .ThenInclude(oi => oi.OrderTable)
                    .AsQueryable();

                // Áp dụng filters
                if (filter.ProductId.HasValue)
                    query = query.Where(r => r.ProductId == filter.ProductId.Value);

                if (filter.UserId.HasValue)
                    query = query.Where(r => r.UserId == filter.UserId.Value);

                if (filter.Rating.HasValue)
                    query = query.Where(r => r.Rating == filter.Rating.Value);

                if (filter.IsVerified.HasValue)
                    query = query.Where(r => r.IsVerified == filter.IsVerified.Value);

                if (!string.IsNullOrEmpty(filter.ProductName))
                    query = query.Where(r => r.Product.Name.Contains(filter.ProductName));

                if (!string.IsNullOrEmpty(filter.UserName))
                    query = query.Where(r => r.User.Name.Contains(filter.UserName));

                if (!string.IsNullOrEmpty(filter.Brand))
                    query = query.Where(r => r.Product.Brand.Contains(filter.Brand));

                if (filter.FromDate.HasValue)
                    query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);

                if (filter.ToDate.HasValue)
                    query = query.Where(r => r.CreatedAt <= filter.ToDate.Value);

                if (!string.IsNullOrEmpty(filter.SearchKeyword))
                {
                    query = query.Where(r => 
                        r.Comment.Contains(filter.SearchKeyword) ||
                        r.Product.Name.Contains(filter.SearchKeyword) ||
                        r.User.Name.Contains(filter.SearchKeyword));
                }

                // Đếm tổng số bản ghi
                var totalCount = await query.CountAsync();

                // Sắp xếp
                query = filter.SortBy.ToLower() switch
                {
                    "rating" => filter.SortOrder == "desc" ? query.OrderByDescending(r => r.Rating) : query.OrderBy(r => r.Rating),
                    "productname" => filter.SortOrder == "desc" ? query.OrderByDescending(r => r.Product.Name) : query.OrderBy(r => r.Product.Name),
                    "username" => filter.SortOrder == "desc" ? query.OrderByDescending(r => r.User.Name) : query.OrderBy(r => r.User.Name),
                    _ => filter.SortOrder == "desc" ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt)
                };

                // Phân trang
                var reviews = await query
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select(r => new ReviewManagementDto
                    {
                        ReviewId = r.ReviewId,
                        UserId = r.UserId,
                        UserName = r.User.Name,
                        UserEmail = r.User.Email,
                        ProductId = r.ProductId,
                        ProductName = r.Product.Name,
                        ProductBrand = r.Product.Brand,
                        ProductImageUrl = r.Product.ImageUrl,
                        OrderItemId = r.OrderItemId,
                        OrderId = r.OrderItem.OrderId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        IsVerified = r.IsVerified,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt,
                        OrderStatus = r.OrderItem.OrderTable.Status
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách đánh giá thành công",
                    data = reviews,
                    pagination = new
                    {
                        page = filter.Page,
                        pageSize = filter.PageSize,
                        totalCount = totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / filter.PageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách đánh giá");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy danh sách đánh giá",
                    error = ex.Message
                });
            }
        }

        // GET: api/admin/ReviewManagement/{id} - Lấy chi tiết một đánh giá
        [HttpGet("{id}")]
        public async Task<IActionResult> GetReviewById(int id)
        {
            try
            {
                var review = await _context.Reviews
                    .Include(r => r.User)
                    .Include(r => r.Product)
                    .ThenInclude(p => p.Category)
                    .Include(r => r.OrderItem)
                    .ThenInclude(oi => oi.OrderTable)
                    .Where(r => r.ReviewId == id)
                    .Select(r => new ReviewManagementDto
                    {
                        ReviewId = r.ReviewId,
                        UserId = r.UserId,
                        UserName = r.User.Name,
                        UserEmail = r.User.Email,
                        ProductId = r.ProductId,
                        ProductName = r.Product.Name,
                        ProductBrand = r.Product.Brand,
                        ProductImageUrl = r.Product.ImageUrl,
                        OrderItemId = r.OrderItemId,
                        OrderId = r.OrderItem.OrderId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        IsVerified = r.IsVerified,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt,
                        OrderStatus = r.OrderItem.OrderTable.Status
                    })
                    .FirstOrDefaultAsync();

                if (review == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy thông tin đánh giá thành công",
                    data = review
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin đánh giá với ID {ReviewId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy thông tin đánh giá",
                    error = ex.Message
                });
            }
        }

        // PUT: api/admin/ReviewManagement/{id}/verify - Duyệt đánh giá
        [HttpPut("{id}/verify")]
        public async Task<IActionResult> VerifyReview(int id, [FromBody] ReviewActionDto actionDto)
        {
            try
            {
                var review = await _context.Reviews.FindAsync(id);
                if (review == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá"
                    });
                }

                review.IsVerified = true;
                review.UpdatedAt = DateTime.Now;

                _context.Reviews.Update(review);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin đã duyệt đánh giá ID {ReviewId}", id);

                return Ok(new
                {
                    success = true,
                    message = "Duyệt đánh giá thành công",
                    data = new
                    {
                        reviewId = review.ReviewId,
                        isVerified = review.IsVerified,
                        updatedAt = review.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi duyệt đánh giá với ID {ReviewId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi duyệt đánh giá",
                    error = ex.Message
                });
            }
        }

        // PUT: api/admin/ReviewManagement/{id}/unverify - Hủy duyệt đánh giá
        [HttpPut("{id}/unverify")]
        public async Task<IActionResult> UnverifyReview(int id, [FromBody] ReviewActionDto actionDto)
        {
            try
            {
                var review = await _context.Reviews.FindAsync(id);
                if (review == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá"
                    });
                }

                review.IsVerified = false;
                review.UpdatedAt = DateTime.Now;

                _context.Reviews.Update(review);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin đã hủy duyệt đánh giá ID {ReviewId}", id);

                return Ok(new
                {
                    success = true,
                    message = "Hủy duyệt đánh giá thành công",
                    data = new
                    {
                        reviewId = review.ReviewId,
                        isVerified = review.IsVerified,
                        updatedAt = review.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi hủy duyệt đánh giá với ID {ReviewId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi hủy duyệt đánh giá",
                    error = ex.Message
                });
            }
        }

        // DELETE: api/admin/ReviewManagement/{id} - Xóa đánh giá
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteReview(int id)
        {
            try
            {
                var review = await _context.Reviews.FindAsync(id);
                if (review == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá"
                    });
                }

                _context.Reviews.Remove(review);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin đã xóa đánh giá ID {ReviewId}", id);

                return Ok(new
                {
                    success = true,
                    message = "Xóa đánh giá thành công"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa đánh giá với ID {ReviewId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi xóa đánh giá",
                    error = ex.Message
                });
            }
        }

        // PUT: api/admin/ReviewManagement/bulk-verify - Duyệt nhiều đánh giá
        [HttpPut("bulk-verify")]
        public async Task<IActionResult> BulkVerifyReviews([FromBody] BulkReviewActionDto actionDto)
        {
            try
            {
                if (!actionDto.ReviewIds.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Danh sách ID đánh giá không được để trống"
                    });
                }

                var reviews = await _context.Reviews
                    .Where(r => actionDto.ReviewIds.Contains(r.ReviewId))
                    .ToListAsync();

                if (!reviews.Any())
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá nào"
                    });
                }

                foreach (var review in reviews)
                {
                    review.IsVerified = true;
                    review.UpdatedAt = DateTime.Now;
                }

                _context.Reviews.UpdateRange(reviews);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin đã duyệt {Count} đánh giá", reviews.Count);

                return Ok(new
                {
                    success = true,
                    message = $"Duyệt {reviews.Count} đánh giá thành công",
                    data = new
                    {
                        verifiedCount = reviews.Count,
                        reviewIds = reviews.Select(r => r.ReviewId).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi duyệt nhiều đánh giá");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi duyệt nhiều đánh giá",
                    error = ex.Message
                });
            }
        }

        // DELETE: api/admin/ReviewManagement/bulk-delete - Xóa nhiều đánh giá
        [HttpDelete("bulk-delete")]
        public async Task<IActionResult> BulkDeleteReviews([FromBody] BulkReviewActionDto actionDto)
        {
            try
            {
                if (!actionDto.ReviewIds.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Danh sách ID đánh giá không được để trống"
                    });
                }

                var reviews = await _context.Reviews
                    .Where(r => actionDto.ReviewIds.Contains(r.ReviewId))
                    .ToListAsync();

                if (!reviews.Any())
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đánh giá nào"
                    });
                }

                _context.Reviews.RemoveRange(reviews);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin đã xóa {Count} đánh giá", reviews.Count);

                return Ok(new
                {
                    success = true,
                    message = $"Xóa {reviews.Count} đánh giá thành công",
                    data = new
                    {
                        deletedCount = reviews.Count
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa nhiều đánh giá");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi xóa nhiều đánh giá",
                    error = ex.Message
                });
            }
        }

        // GET: api/admin/ReviewManagement/statistics - Thống kê đánh giá
        [HttpGet("statistics")]
        public async Task<IActionResult> GetReviewStatistics()
        {
            try
            {
                var now = DateTime.Now;
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                var startOfLastMonth = startOfMonth.AddMonths(-1);

                var totalReviews = await _context.Reviews.CountAsync();
                var verifiedReviews = await _context.Reviews.CountAsync(r => r.IsVerified);
                var unverifiedReviews = totalReviews - verifiedReviews;
                
                // Đánh giá chờ duyệt (chưa verify và order đã completed)
                var pendingReviews = await _context.Reviews
                    .Include(r => r.OrderItem)
                    .ThenInclude(oi => oi.OrderTable)
                    .CountAsync(r => !r.IsVerified && r.OrderItem.OrderTable.Status == "completed");

                var averageRating = await _context.Reviews
                    .Where(r => r.IsVerified)
                    .AverageAsync(r => (double?)r.Rating) ?? 0.0;

                // Phân bố rating
                var ratingDistribution = await _context.Reviews
                    .Where(r => r.IsVerified)
                    .GroupBy(r => r.Rating)
                    .ToDictionaryAsync(g => g.Key, g => g.Count());

                // Đánh giá tháng này và tháng trước
                var reviewsThisMonth = await _context.Reviews
                    .CountAsync(r => r.CreatedAt >= startOfMonth);
                
                var reviewsLastMonth = await _context.Reviews
                    .CountAsync(r => r.CreatedAt >= startOfLastMonth && r.CreatedAt < startOfMonth);

                // Top sản phẩm được đánh giá nhiều nhất
                var topReviewedProducts = await _context.Reviews
                    .Include(r => r.Product)
                    .Where(r => r.IsVerified)
                    .GroupBy(r => new { r.ProductId, r.Product.Name, r.Product.Brand, r.Product.ImageUrl })
                    .Select(g => new TopReviewedProductDto
                    {
                        ProductId = g.Key.ProductId,
                        ProductName = g.Key.Name,
                        Brand = g.Key.Brand,
                        ImageUrl = g.Key.ImageUrl,
                        ReviewCount = g.Count(),
                        AverageRating = g.Average(r => (double)r.Rating)
                    })
                    .OrderByDescending(p => p.ReviewCount)
                    .Take(5)
                    .ToListAsync();

                // Top người dùng đánh giá nhiều nhất
                var mostActiveReviewers = await _context.Reviews
                    .Include(r => r.User)
                    .GroupBy(r => new { r.UserId, r.User.Name, r.User.Email })
                    .Select(g => new MostActiveReviewerDto
                    {
                        UserId = g.Key.UserId,
                        UserName = g.Key.Name,
                        UserEmail = g.Key.Email,
                        ReviewCount = g.Count(),
                        AverageRating = g.Average(r => (double)r.Rating),
                        VerifiedReviewCount = g.Count(r => r.IsVerified)
                    })
                    .OrderByDescending(u => u.ReviewCount)
                    .Take(5)
                    .ToListAsync();

                var statistics = new ReviewStatisticsDto
                {
                    TotalReviews = totalReviews,
                    VerifiedReviews = verifiedReviews,
                    UnverifiedReviews = unverifiedReviews,
                    PendingReviews = pendingReviews,
                    AverageRating = Math.Round(averageRating, 1),
                    RatingDistribution = ratingDistribution,
                    ReviewsThisMonth = reviewsThisMonth,
                    ReviewsLastMonth = reviewsLastMonth,
                    TopReviewedProducts = topReviewedProducts,
                    MostActiveReviewers = mostActiveReviewers
                };

                return Ok(new
                {
                    success = true,
                    message = "Lấy thống kê đánh giá thành công",
                    data = statistics
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thống kê đánh giá");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy thống kê đánh giá",
                    error = ex.Message
                });
            }
        }

        // GET: api/admin/ReviewManagement/product/{productId} - Lấy tổng quan đánh giá của một sản phẩm
        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetProductReviewSummary(int productId)
        {
            try
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(p => p.ProductId == productId);

                if (product == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy sản phẩm"
                    });
                }

                var totalReviews = await _context.Reviews
                    .CountAsync(r => r.ProductId == productId);
                
                var verifiedReviews = await _context.Reviews
                    .CountAsync(r => r.ProductId == productId && r.IsVerified);

                var averageRating = await _context.Reviews
                    .Where(r => r.ProductId == productId && r.IsVerified)
                    .AverageAsync(r => (double?)r.Rating) ?? 0.0;

                // Phân bố rating cho sản phẩm này
                var ratingDistribution = await _context.Reviews
                    .Where(r => r.ProductId == productId && r.IsVerified)
                    .GroupBy(r => r.Rating)
                    .ToDictionaryAsync(g => g.Key, g => g.Count());

                // Đánh giá gần đây nhất
                var recentReviews = await _context.Reviews
                    .Include(r => r.User)
                    .Include(r => r.OrderItem)
                    .ThenInclude(oi => oi.OrderTable)
                    .Where(r => r.ProductId == productId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(10)
                    .Select(r => new ReviewManagementDto
                    {
                        ReviewId = r.ReviewId,
                        UserId = r.UserId,
                        UserName = r.User.Name,
                        UserEmail = r.User.Email,
                        ProductId = r.ProductId,
                        ProductName = product.Name,
                        ProductBrand = product.Brand,
                        ProductImageUrl = product.ImageUrl,
                        OrderItemId = r.OrderItemId,
                        OrderId = r.OrderItem.OrderId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        IsVerified = r.IsVerified,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt,
                        OrderStatus = r.OrderItem.OrderTable.Status
                    })
                    .ToListAsync();

                var summary = new ProductReviewSummaryDto
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    Brand = product.Brand,
                    ImageUrl = product.ImageUrl,
                    TotalReviews = totalReviews,
                    VerifiedReviews = verifiedReviews,
                    AverageRating = Math.Round(averageRating, 1),
                    RatingDistribution = ratingDistribution,
                    RecentReviews = recentReviews
                };

                return Ok(new
                {
                    success = true,
                    message = "Lấy tổng quan đánh giá sản phẩm thành công",
                    data = summary
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy tổng quan đánh giá sản phẩm {ProductId}", productId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy tổng quan đánh giá sản phẩm",
                    error = ex.Message
                });
            }
        }
    }
}
