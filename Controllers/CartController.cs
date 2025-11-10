using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CartController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CartController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Cart/my - Get current user's cart
        [HttpGet("my")]
        public async Task<IActionResult> GetMyCart()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var cart = await _context.Carts.Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (cart == null)
            {
                return Ok(new { success = true, message = "Chưa có giỏ hàng", data = new CartDto { UserId = userId } });
            }
            var cartDto = new CartDto
            {
                CartId = cart.CartId,
                UserId = cart.UserId,
                Items = cart.Items.Select(i => new CartItemDto
                {
                    CartItemId = i.CartItemId,
                    ProductId = i.ProductId,
                    Quantity = i.Quantity
                }).ToList()
            };
            return Ok(new { success = true, message = "Lấy giỏ hàng thành công", data = cartDto });
        }

        // POST: api/Cart/add - Add or update item in cart
        [HttpPost("add")]
        public async Task<IActionResult> AddOrUpdateCartItem([FromBody] CartItemDto itemDto)
        {
            if (itemDto == null || itemDto.ProductId <= 0 || itemDto.Quantity <= 0)
            {
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ" });
            }

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var product = await _context.Products.FindAsync(itemDto.ProductId);
            if (product == null)
            {
                return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });
            }
            if (product.StockQuantity <= 0)
            {
                return BadRequest(new { success = false, message = "Sản phẩm đã hết hàng" });
            }
            if (itemDto.Quantity > product.StockQuantity)
            {
                return BadRequest(new { success = false, message = $"Chỉ còn {product.StockQuantity} sản phẩm trong kho" });
            }

            var cart = await _context.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (cart == null)
            {
                cart = new Cart { UserId = userId };
                _context.Carts.Add(cart);
                await _context.SaveChangesAsync();
            }

            var cartItem = cart.Items.FirstOrDefault(i => i.ProductId == itemDto.ProductId);
            if (cartItem == null)
            {
                cartItem = new CartItem { CartId = cart.CartId, ProductId = itemDto.ProductId, Quantity = itemDto.Quantity };
                _context.CartItems.Add(cartItem);
            }
            else
            {
                cartItem.Quantity = itemDto.Quantity; // Replace quantity (không cộng dồn)
                _context.CartItems.Update(cartItem);
            }

            await _context.SaveChangesAsync();

            // Trả về giỏ hàng cập nhật
            cart = await _context.Carts.Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CartId == cart.CartId);

            var cartDto = new CartDto
            {
                CartId = cart!.CartId,
                UserId = cart.UserId,
                Items = cart.Items.Select(i => new CartItemDto
                {
                    CartItemId = i.CartItemId,
                    ProductId = i.ProductId,
                    Quantity = i.Quantity
                }).ToList()
            };

            return Ok(new { success = true, message = "Cập nhật giỏ hàng thành công", data = cartDto });
        }

        // DELETE: api/Cart/item/{cartItemId} - Remove item from cart
        [HttpDelete("item/{cartItemId}")]
        public async Task<IActionResult> RemoveCartItem(int cartItemId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var cartItem = await _context.CartItems.Include(i => i.Cart)
                .FirstOrDefaultAsync(i => i.CartItemId == cartItemId && i.Cart != null && i.Cart.UserId == userId);
            if (cartItem == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy sản phẩm trong giỏ hàng" });
            }
            _context.CartItems.Remove(cartItem);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Xóa sản phẩm khỏi giỏ hàng thành công" });
        }

        // DELETE: api/Cart/clear - Clear all items in cart
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCart()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var cart = await _context.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (cart == null || !cart.Items.Any())
            {
                return Ok(new { success = true, message = "Giỏ hàng đã trống" });
            }
            _context.CartItems.RemoveRange(cart.Items);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đã xóa toàn bộ giỏ hàng" });
        }
    }
}
