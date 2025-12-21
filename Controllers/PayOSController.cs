using Microsoft.AspNetCore.Mvc;
using TechstoreBackend.Services;
using TechstoreBackend.Models.DTOs;

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
                string description = $"Thanh toan don {orderCode}";

                // Gọi service đã sửa (Lưu ý: hàm CreatePaymentLink bên Service giờ trả về object)
                var paymentResult = await _payOSService.CreatePaymentLink(
                    orderCode,
                    body.Price,
                    description,
                    "http://localhost:5173/success",
                    "http://localhost:5173/cancel"
                );

                // Trả nguyên cục data này về cho React
                return Ok(paymentResult);
            }
            catch (Exception exception)
            {
                return BadRequest(new { message = exception.Message });
            }
        }
    }
}