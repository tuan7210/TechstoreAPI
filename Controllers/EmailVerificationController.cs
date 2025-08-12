using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using TechstoreBackend.Services;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailVerificationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<EmailVerificationController> _logger;

        public EmailVerificationController(
            AppDbContext context,
            IEmailService emailService,
            ILogger<EmailVerificationController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        // POST: api/EmailVerification/send
        [HttpPost("send")]
        public async Task<IActionResult> SendVerificationCode([FromBody] RegisterRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Email))
                {
                    return BadRequest(new { success = false, message = "Email là bắt buộc" });
                }

                // Kiểm tra xem email đã tồn tại trong hệ thống chưa
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                if (existingUser != null)
                {
                    return BadRequest(new { success = false, message = "Email này đã được sử dụng" });
                }

                // Tạo mã xác thực ngẫu nhiên
                string verificationCode = _emailService.GenerateVerificationCode();
                
                // Thời gian hết hạn (10 phút từ bây giờ)
                DateTime expiryTime = DateTime.Now.AddMinutes(10);
                
                // Lưu mã xác thực vào cơ sở dữ liệu
                var verification = new VerificationCode
                {
                    Email = request.Email,
                    Code = verificationCode,
                    ExpiryTime = expiryTime,
                    IsUsed = false
                };
                
                // Xóa các mã cũ của email này nếu có
                var oldCodes = await _context.VerificationCodes
                    .Where(v => v.Email == request.Email && !v.IsUsed)
                    .ToListAsync();
                
                if (oldCodes.Any())
                {
                    _context.VerificationCodes.RemoveRange(oldCodes);
                }
                
                _context.VerificationCodes.Add(verification);
                await _context.SaveChangesAsync();
                
                // Tạo nội dung email
                string emailBody = _emailService.GenerateVerificationEmailBody(
                    request.Name ?? "Khách hàng",
                    verificationCode
                );
                
                // Gửi email
                bool emailSent = await _emailService.SendEmailAsync(
                    request.Email,
                    "Xác thực email - TechStore",
                    emailBody
                );
                
                if (!emailSent)
                {
                    return StatusCode(500, new { success = false, message = "Không thể gửi email xác thực" });
                }
                
                return Ok(new
                {
                    success = true,
                    message = "Mã xác thực đã được gửi đến email của bạn",
                    data = new
                    {
                        email = request.Email,
                        expiryTime
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending verification code");
                return StatusCode(500, new { success = false, message = "Lỗi khi gửi mã xác thực: " + ex.Message });
            }
        }
        
        // POST: api/EmailVerification/verify
        [HttpPost("verify")]
        public async Task<IActionResult> VerifyCode([FromBody] EmailVerificationRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.VerificationCode))
                {
                    return BadRequest(new { success = false, message = "Email và mã xác thực là bắt buộc" });
                }
                
                // Tìm mã xác thực trong cơ sở dữ liệu
                var verification = await _context.VerificationCodes
                    .Where(v => v.Email == request.Email && v.Code == request.VerificationCode && !v.IsUsed)
                    .OrderByDescending(v => v.ExpiryTime)
                    .FirstOrDefaultAsync();
                
                if (verification == null)
                {
                    return BadRequest(new { success = false, message = "Mã xác thực không hợp lệ" });
                }
                
                // Kiểm tra thời gian hết hạn
                if (verification.ExpiryTime < DateTime.Now)
                {
                    return BadRequest(new { success = false, message = "Mã xác thực đã hết hạn" });
                }
                
                // Đánh dấu đã sử dụng
                verification.IsUsed = true;
                _context.VerificationCodes.Update(verification);
                await _context.SaveChangesAsync();
                
                return Ok(new
                {
                    success = true,
                    message = "Xác thực email thành công"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying code");
                return StatusCode(500, new { success = false, message = "Lỗi khi xác thực mã: " + ex.Message });
            }
        }
    }
}
