using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class LoginRequest
    {
        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
        public string Password { get; set; } = string.Empty;
    }
}
