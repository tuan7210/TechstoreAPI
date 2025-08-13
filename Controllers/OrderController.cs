using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using System.Security.Claims;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OrderController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Order - Lấy danh sách đơn hàng (admin)
        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAllOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10, 
            [FromQuery] string? status = null)
        {
            try
            {
                var query = _context.OrderTables
                    .Include(o => o.User)
                    .AsQueryable();

                // Lọc theo status nếu có
                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(o => o.Status == status);
                }

                // Tổng số đơn hàng
                var totalCount = await query.CountAsync();

                // Phân trang
                var orders = await query
                    .OrderByDescending(o => o.OrderDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var orderDtos = new List<OrderResponseDto>();

                foreach (var order in orders)
                {
                    var items = await _context.OrderItems
                        .Include(oi => oi.Product)
                        .Where(oi => oi.OrderId == order.OrderId)
                        .ToListAsync();

                    var itemDtos = items.Select(item => new OrderItemResponseDto
                    {
                        OrderItemId = item.OrderItemId,
                        ProductId = item.ProductId,
                        ProductName = item.Product?.Name ?? "Unknown",
                        ImageUrl = item.Product?.ImageUrl ?? "",
                        Quantity = item.Quantity,
                        Price = item.Price,
                        Subtotal = item.Price * item.Quantity
                    }).ToList();

                    orderDtos.Add(new OrderResponseDto
                    {
                        OrderId = order.OrderId,
                        UserId = order.UserId,
                        UserName = order.User?.Name ?? "Unknown",
                        OrderDate = order.OrderDate,
                        Status = order.Status,
                        TotalAmount = order.TotalAmount,
                        ShippingAddress = order.ShippingAddress,
                        PaymentStatus = order.PaymentStatus,
                        PaymentMethod = order.PaymentMethod,
                        Items = itemDtos
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách đơn hàng thành công",
                    data = orderDtos,
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
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy danh sách đơn hàng",
                    error = ex.Message
                });
            }
        }

        // GET: api/Order/my - Lấy danh sách đơn hàng của người dùng hiện tại
        [HttpGet("my")]
        public async Task<IActionResult> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

                var query = _context.OrderTables
                    .Where(o => o.UserId == currentUserId);

                // Tổng số đơn hàng
                var totalCount = await query.CountAsync();

                // Phân trang
                var orders = await query
                    .OrderByDescending(o => o.OrderDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var orderDtos = new List<OrderResponseDto>();

                foreach (var order in orders)
                {
                    var items = await _context.OrderItems
                        .Include(oi => oi.Product)
                        .Where(oi => oi.OrderId == order.OrderId)
                        .ToListAsync();

                    var itemDtos = items.Select(item => new OrderItemResponseDto
                    {
                        OrderItemId = item.OrderItemId,
                        ProductId = item.ProductId,
                        ProductName = item.Product?.Name ?? "Unknown",
                        ImageUrl = item.Product?.ImageUrl ?? "",
                        Quantity = item.Quantity,
                        Price = item.Price,
                        Subtotal = item.Price * item.Quantity
                    }).ToList();

                    orderDtos.Add(new OrderResponseDto
                    {
                        OrderId = order.OrderId,
                        UserId = order.UserId,
                        UserName = "", // Không cần thiết cho user xem đơn hàng của mình
                        OrderDate = order.OrderDate,
                        Status = order.Status,
                        TotalAmount = order.TotalAmount,
                        ShippingAddress = order.ShippingAddress,
                        PaymentStatus = order.PaymentStatus,
                        PaymentMethod = order.PaymentMethod,
                        Items = itemDtos
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách đơn hàng thành công",
                    data = orderDtos,
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
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy danh sách đơn hàng",
                    error = ex.Message
                });
            }
        }

        // GET: api/Order/{id} - Lấy thông tin chi tiết đơn hàng
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrderById(int id)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
                var isAdmin = User.IsInRole("admin");

                var order = await _context.OrderTables
                    .Include(o => o.User)
                    .FirstOrDefaultAsync(o => o.OrderId == id);

                if (order == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đơn hàng"
                    });
                }

                // Kiểm tra quyền: chỉ admin hoặc chủ đơn hàng mới có thể xem
                if (!isAdmin && currentUserId != order.UserId)
                {
                    return Forbid();
                }

                var items = await _context.OrderItems
                    .Include(oi => oi.Product)
                    .Where(oi => oi.OrderId == id)
                    .ToListAsync();

                var itemDtos = items.Select(item => new OrderItemResponseDto
                {
                    OrderItemId = item.OrderItemId,
                    ProductId = item.ProductId,
                    ProductName = item.Product?.Name ?? "Unknown",
                    ImageUrl = item.Product?.ImageUrl ?? "",
                    Quantity = item.Quantity,
                    Price = item.Price,
                    Subtotal = item.Price * item.Quantity
                }).ToList();

                var orderDto = new OrderResponseDto
                {
                    OrderId = order.OrderId,
                    UserId = order.UserId,
                    UserName = order.User?.Name ?? "Unknown",
                    OrderDate = order.OrderDate,
                    Status = order.Status,
                    TotalAmount = order.TotalAmount,
                    ShippingAddress = order.ShippingAddress,
                    PaymentStatus = order.PaymentStatus,
                    PaymentMethod = order.PaymentMethod,
                    Items = itemDtos
                };

                return Ok(new
                {
                    success = true,
                    message = "Lấy thông tin đơn hàng thành công",
                    data = orderDto
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi lấy thông tin đơn hàng",
                    error = ex.Message
                });
            }
        }

        // POST: api/Order - Tạo đơn hàng mới
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto orderDto)
        {
            try
            {
                // Kiểm tra dữ liệu đầu vào
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Dữ liệu không hợp lệ",
                        errors = ModelState.Values
                            .SelectMany(v => v.Errors)
                            .Select(e => e.ErrorMessage)
                            .ToList()
                    });
                }
                
                // Ghi log dữ liệu nhận được để debug
                Console.WriteLine($"Received order data: UserId={orderDto.UserId}, Items={orderDto.Items?.Count ?? 0}, ShippingAddress={orderDto.ShippingAddress}, PaymentMethod={orderDto.PaymentMethod}");
                
                // Kiểm tra null cho Items
                if (orderDto.Items == null || orderDto.Items.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Đơn hàng phải có ít nhất một sản phẩm"
                    });
                }

                var currentUserId = int.Parse(User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0");
                var isAdmin = User.IsInRole("admin");

                // Luôn sử dụng currentUserId từ token JWT thay vì từ request
                // trừ khi người dùng là admin và rõ ràng đang đặt hàng cho người khác
                var orderUserId = currentUserId;
                
                if (isAdmin && orderDto.UserId != 0 && orderDto.UserId != currentUserId)
                {
                    // Admin có thể đặt hàng cho người khác nếu muốn
                    orderUserId = orderDto.UserId;
                    Console.WriteLine($"Admin {currentUserId} đang tạo đơn hàng cho user {orderUserId}");
                }
                else
                {
                    // Ghi đè userId trong DTO bằng userId từ token
                    orderDto.UserId = currentUserId;
                    Console.WriteLine($"Sử dụng userId từ token: {currentUserId}");
                }

                // Kiểm tra người dùng tồn tại
                var user = await _context.Users.FindAsync(orderUserId);
                if (user == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Người dùng không tồn tại"
                    });
                }

                // Kiểm tra sản phẩm và tính toán tổng tiền
                decimal totalAmount = 0;
                var orderItems = new List<OrderItem>();

                foreach (var item in orderDto.Items)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product == null)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Sản phẩm với ID {item.ProductId} không tồn tại"
                        });
                    }

                    if (product.StockQuantity < item.Quantity)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Sản phẩm {product.Name} không đủ số lượng trong kho"
                        });
                    }

                    var subtotal = product.Price * item.Quantity;
                    totalAmount += subtotal;

                    orderItems.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Price = product.Price
                    });

                    // Giảm số lượng sản phẩm trong kho
                    product.StockQuantity -= item.Quantity;
                    _context.Products.Update(product);
                }

                // Tạo đơn hàng mới
                var order = new OrderTable
                {
                    UserId = orderUserId, // Sử dụng orderUserId đã xác định ở trên
                    OrderDate = DateTime.Now,
                    Status = "pending",
                    TotalAmount = totalAmount,
                    ShippingAddress = orderDto.ShippingAddress,
                    PaymentStatus = "unpaid",
                    PaymentMethod = orderDto.PaymentMethod
                };

                _context.OrderTables.Add(order);
                await _context.SaveChangesAsync();
                
                // Thêm chi tiết đơn hàng
                foreach (var item in orderItems)
                {
                    item.OrderId = order.OrderId;
                    _context.OrderItems.Add(item);
                }

                await _context.SaveChangesAsync();
/*
                var productIds = orderDto.Items
                    .Select(i => i.ProductId)
                    .Distinct()
                    .ToList();

                var products = _context.Products
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToList();

                foreach (var product in products)
                {
                    product.StockQuantity -= orderDto.Items
                        .Where(i => i.ProductId == product.ProductId)
                        .Select(i => i.Quantity)
                        .Sum();
                    _context.Products.Update(product);
                }

                await _context.SaveChangesAsync();
*/
                // Trả về thông tin đơn hàng
                var itemDtos = new List<OrderItemResponseDto>();
                foreach (var item in orderItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    itemDtos.Add(new OrderItemResponseDto
                    {
                        OrderItemId = item.OrderItemId,
                        ProductId = item.ProductId,
                        ProductName = product?.Name ?? "Unknown",
                        ImageUrl = product?.ImageUrl ?? "",
                        Quantity = item.Quantity,
                        Price = item.Price,
                        Subtotal = item.Price * item.Quantity
                    });
                }

                var orderDto2 = new OrderResponseDto
                {
                    OrderId = order.OrderId,
                    UserId = order.UserId,
                    UserName = user.Name,
                    OrderDate = order.OrderDate,
                    Status = order.Status,
                    TotalAmount = order.TotalAmount,
                    ShippingAddress = order.ShippingAddress,
                    PaymentStatus = order.PaymentStatus,
                    PaymentMethod = order.PaymentMethod,
                    Items = itemDtos
                };

                return CreatedAtAction(nameof(GetOrderById), new { id = order.OrderId }, new
                {
                    success = true,
                    message = "Tạo đơn hàng thành công",
                    data = orderDto2
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi tạo đơn hàng",
                    error = ex.Message
                });
            }
        }

        // PUT: api/Order/{id}/status - Cập nhật trạng thái đơn hàng (admin only)
        [HttpPut("{id}/status")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] OrderStatusUpdateDto updateDto)
        {
            try
            {
                var order = await _context.OrderTables.FindAsync(id);
                if (order == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đơn hàng"
                    });
                }

                // Validate status
                var validStatuses = new[] { "pending", "processing", "shipped", "delivered", "canceled" };
                if (!validStatuses.Contains(updateDto.Status))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Trạng thái đơn hàng không hợp lệ"
                    });
                }

                order.Status = updateDto.Status;
                
                // Nếu đơn hàng bị hủy, hoàn lại số lượng sản phẩm
                if (updateDto.Status == "canceled" && order.Status != "canceled")
                {
                    var orderItems = await _context.OrderItems
                        .Where(oi => oi.OrderId == id)
                        .ToListAsync();

                    foreach (var item in orderItems)
                    {
                        var product = await _context.Products.FindAsync(item.ProductId);
                        if (product != null)
                        {
                            product.StockQuantity += item.Quantity;
                            _context.Products.Update(product);
                        }
                    }
                }

                // Nếu trạng thái là delivered, đánh dấu là đã thanh toán
                if (updateDto.Status == "delivered")
                {
                    order.PaymentStatus = "paid";
                }

                _context.OrderTables.Update(order);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật trạng thái đơn hàng thành công",
                    data = new
                    {
                        orderId = order.OrderId,
                        status = order.Status,
                        paymentStatus = order.PaymentStatus
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi cập nhật trạng thái đơn hàng",
                    error = ex.Message
                });
            }
        }

        // PUT: api/Order/{id}/payment - Cập nhật trạng thái thanh toán (admin only)
        [HttpPut("{id}/payment")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] PaymentStatusUpdateDto updateDto)
        {
            try
            {
                var order = await _context.OrderTables.FindAsync(id);
                if (order == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đơn hàng"
                    });
                }

                // Validate status
                var validStatuses = new[] { "unpaid", "paid", "refunded" };
                if (!validStatuses.Contains(updateDto.PaymentStatus))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Trạng thái thanh toán không hợp lệ"
                    });
                }

                order.PaymentStatus = updateDto.PaymentStatus;
                _context.OrderTables.Update(order);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật trạng thái thanh toán thành công",
                    data = new
                    {
                        orderId = order.OrderId,
                        paymentStatus = order.PaymentStatus
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi cập nhật trạng thái thanh toán",
                    error = ex.Message
                });
            }
        }

        // DELETE: api/Order/{id} - Xóa đơn hàng (admin only)
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            try
            {
                var order = await _context.OrderTables.FindAsync(id);
                if (order == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy đơn hàng"
                    });
                }

                // Xóa chi tiết đơn hàng
                var orderItems = await _context.OrderItems.Where(oi => oi.OrderId == id).ToListAsync();
                _context.OrderItems.RemoveRange(orderItems);

                // Hoàn lại số lượng sản phẩm
                foreach (var item in orderItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity += item.Quantity;
                        _context.Products.Update(product);
                    }
                }

                // Xóa đơn hàng
                _context.OrderTables.Remove(order);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Xóa đơn hàng thành công"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi khi xóa đơn hàng",
                    error = ex.Message
                });
            }
        }
    }

    public class OrderStatusUpdateDto
    {
        [Required(ErrorMessage = "Trạng thái đơn hàng là bắt buộc")]
        public string Status { get; set; } = string.Empty;
    }

    public class PaymentStatusUpdateDto
    {
        [Required(ErrorMessage = "Trạng thái thanh toán là bắt buộc")]
        public string PaymentStatus { get; set; } = string.Empty;
    }
}
