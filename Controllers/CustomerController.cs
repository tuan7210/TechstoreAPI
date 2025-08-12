using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Yêu cầu đăng nhập
    public class CustomerController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CustomerController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Customer - Lấy danh sách tất cả khách hàng (Admin only)
        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAllCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var query = _context.Users.Where(u => u.Role == "customer");

                // Tổng số khách hàng
                var totalCount = await query.CountAsync();

                // Phân trang
                var customers = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        u.Phone,
                        u.Address,
                        u.CreatedAt,
                        OrderCount = _context.OrderTables.Count(o => o.UserId == u.UserId),
                        TotalSpent = _context.OrderTables
                            .Where(o => o.UserId == u.UserId && o.PaymentStatus == "paid")
                            .Sum(o => o.TotalAmount)
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách khách hàng thành công",
                    data = customers,
                    pagination = new
                    {
                        page = page,
                        pageSize = pageSize,
                        totalCount = totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi lấy danh sách khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // GET: api/Customer/{id} - Lấy thông tin chi tiết khách hàng
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCustomerById(int id)
        {
            try
            {
                // Kiểm tra quyền: chỉ admin hoặc chính khách hàng đó mới có thể xem thông tin
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                var isAdmin = User.IsInRole("admin");
                
                if (!isAdmin && currentUserId != id)
                {
                    return Forbid();
                }
                
                var customer = await _context.Users
                    .Where(u => u.UserId == id && u.Role == "customer")
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        u.Phone,
                        u.Address,
                        u.CreatedAt,
                        u.UpdatedAt,
                        u.Status,
                        OrderCount = _context.OrderTables.Count(o => o.UserId == u.UserId),
                        TotalSpent = _context.OrderTables
                            .Where(o => o.UserId == u.UserId && o.PaymentStatus == "paid")
                            .Sum(o => o.TotalAmount),
                        RecentOrders = _context.OrderTables
                            .Where(o => o.UserId == u.UserId)
                            .OrderByDescending(o => o.OrderDate)
                            .Take(5)
                            .Select(o => new
                            {
                                o.OrderId,
                                o.OrderDate,
                                o.Status,
                                o.TotalAmount,
                                o.PaymentStatus
                            })
                    })
                    .FirstOrDefaultAsync();

                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy khách hàng" 
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy thông tin khách hàng thành công",
                    data = customer
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi lấy thông tin khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // PUT: api/Customer/{id} - Cập nhật thông tin khách hàng
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CustomerUpdateDto updateDto)
        {
            try
            {
                // Kiểm tra quyền: chỉ admin hoặc chính khách hàng đó mới có thể cập nhật thông tin
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                var isAdmin = User.IsInRole("admin");
                
                if (!isAdmin && currentUserId != id)
                {
                    return Forbid();
                }
                
                var customer = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id && u.Role == "customer");
                
                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy khách hàng" 
                    });
                }

                // Kiểm tra email trùng lặp (nếu thay đổi email)
                if (!string.IsNullOrEmpty(updateDto.Email) && updateDto.Email != customer.Email)
                {
                    var emailExists = await _context.Users.AnyAsync(u => u.Email == updateDto.Email && u.UserId != id);
                    if (emailExists)
                    {
                        return BadRequest(new { 
                            success = false,
                            message = "Email đã được sử dụng bởi khách hàng khác" 
                        });
                    }
                    customer.Email = updateDto.Email;
                }
                
                // Cập nhật trạng thái tài khoản (chỉ admin mới có quyền)
                if (isAdmin && !string.IsNullOrEmpty(updateDto.Status))
                {
                    if (updateDto.Status == "active" || updateDto.Status == "locked")
                    {
                        customer.Status = updateDto.Status;
                    }
                    else
                    {
                        return BadRequest(new { 
                            success = false,
                            message = "Trạng thái tài khoản không hợp lệ" 
                        });
                    }
                }

                // Cập nhật thông tin
                if (!string.IsNullOrEmpty(updateDto.Name))
                    customer.Name = updateDto.Name;
                
                if (!string.IsNullOrEmpty(updateDto.Phone))
                    customer.Phone = updateDto.Phone;
                
                if (!string.IsNullOrEmpty(updateDto.Address))
                    customer.Address = updateDto.Address;

                customer.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật thông tin khách hàng thành công",
                    data = new
                    {
                        customer.UserId,
                        customer.Name,
                        customer.Email,
                        customer.Phone,
                        customer.Address,
                        customer.UpdatedAt,
                        customer.Status
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi cập nhật thông tin khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // DELETE: api/Customer/{id} - Xóa khách hàng (Admin only)
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            try
            {
                var customer = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id && u.Role == "customer");
                
                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy khách hàng" 
                    });
                }

                // Kiểm tra xem khách hàng có đơn hàng không
                var hasOrders = await _context.OrderTables.AnyAsync(o => o.UserId == id);
                if (hasOrders)
                {
                    return BadRequest(new { 
                        success = false,
                        message = "Không thể xóa khách hàng đã có đơn hàng. Vui lòng vô hiệu hóa tài khoản thay vì xóa." 
                    });
                }

                _context.Users.Remove(customer);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Xóa khách hàng thành công"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi xóa khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // GET: api/Customer/search - Tìm kiếm khách hàng
        [HttpGet("search")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SearchCustomers([FromQuery] string keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                if (string.IsNullOrEmpty(keyword))
                {
                    return BadRequest(new { 
                        success = false,
                        message = "Từ khóa tìm kiếm không được để trống" 
                    });
                }

                var query = _context.Users
                    .Where(u => u.Role == "customer" && 
                               (u.Name.Contains(keyword) || 
                                u.Email.Contains(keyword) || 
                                u.Phone.Contains(keyword)));

                var totalCount = await query.CountAsync();

                var customers = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        u.Phone,
                        u.Address,
                        u.CreatedAt,
                        OrderCount = _context.OrderTables.Count(o => o.UserId == u.UserId),
                        TotalSpent = _context.OrderTables
                            .Where(o => o.UserId == u.UserId && o.PaymentStatus == "paid")
                            .Sum(o => o.TotalAmount)
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Tìm kiếm khách hàng thành công",
                    data = customers,
                    keyword = keyword,
                    pagination = new
                    {
                        page = page,
                        pageSize = pageSize,
                        totalCount = totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi tìm kiếm khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // GET: api/Customer/me - Lấy thông tin cá nhân của khách hàng đăng nhập
        [HttpGet("me")]
        public async Task<IActionResult> GetMyInfo()
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                
                var customer = await _context.Users
                    .Where(u => u.UserId == currentUserId && u.Role == "customer")
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        u.Phone,
                        u.Address,
                        u.CreatedAt,
                        u.UpdatedAt,
                        u.Status,
                        OrderCount = _context.OrderTables.Count(o => o.UserId == u.UserId),
                        TotalSpent = _context.OrderTables
                            .Where(o => o.UserId == u.UserId && o.PaymentStatus == "paid")
                            .Sum(o => o.TotalAmount),
                        RecentOrders = _context.OrderTables
                            .Where(o => o.UserId == u.UserId)
                            .OrderByDescending(o => o.OrderDate)
                            .Take(5)
                            .Select(o => new
                            {
                                o.OrderId,
                                o.OrderDate,
                                o.Status,
                                o.TotalAmount,
                                o.PaymentStatus
                            })
                    })
                    .FirstOrDefaultAsync();

                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy thông tin khách hàng" 
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy thông tin cá nhân thành công",
                    data = customer
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi lấy thông tin cá nhân", 
                    error = ex.Message 
                });
            }
        }

        // PUT: api/Customer/me - Cập nhật thông tin cá nhân của khách hàng đăng nhập
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyInfo([FromBody] CustomerUpdateDto updateDto)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                
                var customer = await _context.Users.FirstOrDefaultAsync(u => u.UserId == currentUserId && u.Role == "customer");
                
                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy thông tin khách hàng" 
                    });
                }

                // Kiểm tra email trùng lặp (nếu thay đổi email)
                if (!string.IsNullOrEmpty(updateDto.Email) && updateDto.Email != customer.Email)
                {
                    var emailExists = await _context.Users.AnyAsync(u => u.Email == updateDto.Email && u.UserId != currentUserId);
                    if (emailExists)
                    {
                        return BadRequest(new { 
                            success = false,
                            message = "Email đã được sử dụng bởi khách hàng khác" 
                        });
                    }
                    customer.Email = updateDto.Email;
                }

                // Cập nhật thông tin
                if (!string.IsNullOrEmpty(updateDto.Name))
                    customer.Name = updateDto.Name;
                
                if (!string.IsNullOrEmpty(updateDto.Phone))
                    customer.Phone = updateDto.Phone;
                
                if (!string.IsNullOrEmpty(updateDto.Address))
                    customer.Address = updateDto.Address;

                customer.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật thông tin khách hàng thành công",
                    data = new
                    {
                        customer.UserId,
                        customer.Name,
                        customer.Email,
                        customer.Phone,
                        customer.Address,
                        customer.UpdatedAt,
                        customer.Status
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi cập nhật thông tin khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // PUT: api/Customer/{id}/lock - Khóa tài khoản khách hàng (Admin only)
        [HttpPut("{id}/lock")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> LockCustomerAccount(int id)
        {
            try
            {
                var customer = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id && u.Role == "customer");
                
                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy khách hàng" 
                    });
                }

                customer.Status = "locked";
                customer.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Khóa tài khoản khách hàng thành công",
                    data = new
                    {
                        customer.UserId,
                        customer.Name,
                        customer.Email,
                        customer.Status,
                        customer.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi khóa tài khoản khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // PUT: api/Customer/{id}/unlock - Mở khóa tài khoản khách hàng (Admin only)
        [HttpPut("{id}/unlock")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UnlockCustomerAccount(int id)
        {
            try
            {
                var customer = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id && u.Role == "customer");
                
                if (customer == null)
                {
                    return NotFound(new { 
                        success = false,
                        message = "Không tìm thấy khách hàng" 
                    });
                }

                customer.Status = "active";
                customer.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Mở khóa tài khoản khách hàng thành công",
                    data = new
                    {
                        customer.UserId,
                        customer.Name,
                        customer.Email,
                        customer.Status,
                        customer.UpdatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi mở khóa tài khoản khách hàng", 
                    error = ex.Message 
                });
            }
        }

        // GET: api/Customer/stats - Thống kê khách hàng (Admin only)
        [HttpGet("stats")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetCustomerStats()
        {
            try
            {
                var totalCustomers = await _context.Users.CountAsync(u => u.Role == "customer");
                var newCustomersThisMonth = await _context.Users
                    .CountAsync(u => u.Role == "customer" && 
                               u.CreatedAt.HasValue && 
                               u.CreatedAt.Value.Month == DateTime.Now.Month &&
                               u.CreatedAt.Value.Year == DateTime.Now.Year);

                var activeCustomers = await _context.Users
                    .Where(u => u.Role == "customer")
                    .CountAsync(u => _context.OrderTables.Any(o => o.UserId == u.UserId));

                var topCustomers = await _context.Users
                    .Where(u => u.Role == "customer")
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Email,
                        TotalSpent = _context.OrderTables
                            .Where(o => o.UserId == u.UserId && o.PaymentStatus == "paid")
                            .Sum(o => o.TotalAmount),
                        OrderCount = _context.OrderTables.Count(o => o.UserId == u.UserId)
                    })
                    .Where(c => c.TotalSpent > 0)
                    .OrderByDescending(c => c.TotalSpent)
                    .Take(10)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Lấy thống kê khách hàng thành công",
                    data = new
                    {
                        totalCustomers,
                        newCustomersThisMonth,
                        activeCustomers,
                        topCustomers
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi khi lấy thống kê khách hàng", 
                    error = ex.Message 
                });
            }
        }
    }
}
