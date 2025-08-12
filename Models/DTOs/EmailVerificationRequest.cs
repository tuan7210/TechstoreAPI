using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class EmailVerificationRequest
    {
        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Mã xác thực là bắt buộc")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã xác thực phải có đúng 6 ký tự")]
        public string VerificationCode { get; set; } = string.Empty;
    }
}
