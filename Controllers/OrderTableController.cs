using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrderTableController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OrderTableController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/OrderTable - Tạo đơn hàng mới (alias của api/Order)
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

                var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
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

                return CreatedAtAction("GetOrderById", "Order", new { id = order.OrderId }, new
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
    }
}
