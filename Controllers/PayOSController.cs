using Microsoft.AspNetCore.Mvc;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using TechstoreBackend.Services; // Import Service vừa tạo

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PayOSController : ControllerBase
    {
        private readonly PayOSService _payOSService;

        public PayOSController(PayOSService payOSService)
        {
            _payOSService = payOSService;
        }

        [HttpPost("create-payment-link")]
        public async Task<IActionResult> CreatePaymentLink([FromBody] CreatePaymentDto body)
        {
            try
            {
                long orderCode = long.Parse(DateTimeOffset.Now.ToString("ffffff"));
                // Quy ước: giá trị truyền vào API phải chính xác
                // description không dấu, không ký tự đặc biệt, tối đa 25 ký tự để an toàn nhất
                string description = $"Thanh toan don {orderCode}"; 

                string checkoutUrl = await _payOSService.CreatePaymentLink(
                    orderCode, 
                    body.Price, 
                    description, 
                    "http://localhost:5173/success", // Link frontend
                    "http://localhost:5173/cancel"   // Link frontend
                );

                return Ok(new { checkoutUrl = checkoutUrl });
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message); // Log lỗi ra console server để debug
                return BadRequest(new { message = exception.Message });
            }
        }
    }
}