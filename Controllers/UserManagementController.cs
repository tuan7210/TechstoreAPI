using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using TechstoreBackend.Data;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class UserManagementController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserManagementController> _logger;

        public UserManagementController(AppDbContext context, ILogger<UserManagementController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/UserManagement - Lấy danh sách tất cả người dùng
        [HttpGet]
        public async Task<IActionResult> GetAllUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, 
            [FromQuery] string? status = null, [FromQuery] string? role = null, [FromQuery] string? search = null)
        {
            try
            {
                var query = _context.Users.AsQueryable();

                // Lọc theo status
                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(u => u.Status == status);
                }

                // Lọc theo role
                if (!string.IsNullOrEmpty(role))
                {
                    query = query.Where(u => u.Role == role);
                }

                // Tìm kiếm theo tên hoặc email
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => u.Name.Contains(search) || u.Email.Contains(search));
                }

                var totalCount = await query.CountAsync();

                var users = await query
                    .OrderByDescending(u => u.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        u.Role,
                        u.Phone,
                        u.Address,
                        u.Status,
                        u.CreatedAt,
                        u.UpdatedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách người dùng thành công",
                    data = users,
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách người dùng");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy danh sách người dùng",
                    error = ex.Message
                });
            }
        }

        // PUT: api/UserManagement/{id}/status - Cập nhật trạng thái tài khoản
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusDto request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Dữ liệu không hợp lệ",
                        errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                    });
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy người dùng"
                    });
                }

                // Không cho phép admin tự khóa chính mình
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                if (user.UserId == currentUserId && request.Status != "active")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Bạn không thể thay đổi trạng thái tài khoản của chính mình"
                    });
                }

                var oldStatus = user.Status;
                user.Status = request.Status;
                user.UpdatedAt = DateTime.Now;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Admin {currentUserId} đã thay đổi trạng thái tài khoản {user.UserId} từ {oldStatus} thành {request.Status}");

                return Ok(new
                {
                    success = true,
                    message = $"Cập nhật trạng thái tài khoản thành công",
                    data = new
                    {
                        userId = user.UserId,
                        name = user.Name,
                        email = user.Email,
                        oldStatus,
                        newStatus = user.Status,
                        reason = request.Reason
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi cập nhật trạng thái tài khoản {id}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi cập nhật trạng thái tài khoản",
                    error = ex.Message
                });
            }
        }

        // GET: api/UserManagement/{id} - Lấy thông tin chi tiết người dùng
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy người dùng"
                    });
                }

                // Lấy thống kê đơn hàng của user
                var orderStats = await _context.OrderTables
                    .Where(o => o.UserId == id)
                    .GroupBy(o => o.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var totalOrders = await _context.OrderTables.CountAsync(o => o.UserId == id);
                var totalSpent = await _context.OrderTables
                    .Where(o => o.UserId == id && o.PaymentStatus == "paid")
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

                return Ok(new
                {
                    success = true,
                    message = "Lấy thông tin người dùng thành công",
                    data = new
                    {
                        user = new
                        {
                            user.UserId,
                            user.Name,
                            user.Email,
                            user.Role,
                            user.Phone,
                            user.Address,
                            user.Status,
                            user.CreatedAt,
                            user.UpdatedAt
                        },
                        statistics = new
                        {
                            totalOrders,
                            totalSpent,
                            ordersByStatus = orderStats
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi lấy thông tin người dùng {id}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy thông tin người dùng",
                    error = ex.Message
                });
            }
        }

        // GET: api/UserManagement/statistics - Thống kê tổng quan người dùng
        [HttpGet("statistics")]
        public async Task<IActionResult> GetUserStatistics()
        {
            try
            {
                var totalUsers = await _context.Users.CountAsync();
                var activeUsers = await _context.Users.CountAsync(u => u.Status == "active");
                var lockedUsers = await _context.Users.CountAsync(u => u.Status == "locked");
                var blockedUsers = await _context.Users.CountAsync(u => u.Status == "blocked");
                
                var adminCount = await _context.Users.CountAsync(u => u.Role == "admin");
                var customerCount = await _context.Users.CountAsync(u => u.Role == "customer");

                // Thống kê người dùng đăng ký theo tháng (6 tháng gần nhất)
                var sixMonthsAgo = DateTime.Now.AddMonths(-6);
                var monthlyRegistrations = await _context.Users
                    .Where(u => u.CreatedAt >= sixMonthsAgo)
                    .GroupBy(u => new { u.CreatedAt!.Value.Year, u.CreatedAt.Value.Month })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Lấy thống kê người dùng thành công",
                    data = new
                    {
                        overview = new
                        {
                            totalUsers,
                            activeUsers,
                            lockedUsers,
                            blockedUsers,
                            adminCount,
                            customerCount
                        },
                        monthlyRegistrations
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thống kê người dùng");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy thống kê người dùng",
                    error = ex.Message
                });
            }
        }
    }

    public class UpdateUserStatusDto
    {
        [Required(ErrorMessage = "Trạng thái là bắt buộc")]
        public string Status { get; set; } = string.Empty; // active, locked, blocked
        
        public string? Reason { get; set; } // Lý do thay đổi trạng thái
    }
}
