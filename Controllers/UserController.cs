using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;

namespace TechstoreBackend.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public UserController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            // Kiểm tra email đã tồn tại chưa
            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            {
                return BadRequest(new { message = "Email đã được sử dụng." });
            }

            // Hash mật khẩu
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Tạo user mới
            var user = new User
            {
                Name = request.Name,
                Email = request.Email,
                PasswordHash = passwordHash,
                Role = "customer",
                Phone = request.Phone,
                Address = request.Address,

            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đăng ký thành công." });
        }

        // Endpoint đăng ký admin (chỉ dùng cho development)
        [HttpPost("register-admin")]
        public async Task<IActionResult> RegisterAdmin(RegisterRequest request)
        {
            try
            {
                // Kiểm tra email đã tồn tại chưa
                if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                {
                    return BadRequest(new { message = "Email đã được sử dụng." });
                }

                // Validate input
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new { message = "Email và mật khẩu không được để trống." });
                }

                if (request.Password.Length < 6)
                {
                    return BadRequest(new { message = "Mật khẩu phải có ít nhất 6 ký tự." });
                }

                // Hash mật khẩu bằng BCrypt
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // Tạo admin mới
                var admin = new User
                {
                    Name = request.Name ?? "Admin",
                    Email = request.Email,
                    PasswordHash = passwordHash,
                    Role = "admin", // Đặt role là admin
                    Phone = request.Phone ?? "0000000000",
                    Address = request.Address ?? "Admin Address",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Users.Add(admin);
                await _context.SaveChangesAsync();

                return Ok(new { 
                    message = "Đăng ký admin thành công.",
                    admin = new {
                        admin.UserId,
                        admin.Name,
                        admin.Email,
                        admin.Role,
                        admin.Phone,
                        admin.Address
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    message = "Lỗi khi đăng ký admin.", 
                    error = ex.Message 
                });
            }
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new { message = "Email và mật khẩu không được để trống." });
                }


                // Debug logging
                Console.WriteLine($"Login attempt for email: '{request.Email}'");
                var totalUsers = await _context.Users.CountAsync();
                Console.WriteLine($"Total users in database: {totalUsers}");

                // Try to find user with exact email match
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                if (user == null)
                {
                    // Try case-insensitive search
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                    if (user == null)
                    {
                        var allEmails = await _context.Users.Select(u => u.Email).ToListAsync();
                        Console.WriteLine($"Available emails in database: {string.Join(", ", allEmails)}");
                        return BadRequest(new {
                            message = "Email không tồn tại.",
                            searchedEmail = request.Email,
                            availableEmails = allEmails,
                            totalUsers = totalUsers
                        });
                    }
                }

                // Kiểm tra trạng thái tài khoản
                if (user.Status == "blocked")
                {
                    return Unauthorized(new {
                        message = "Tài khoản bạn đã tạm thời bị khóa, vui lòng liên hệ admin để biết thêm chi tiết."
                    });
                }

                Console.WriteLine($"User found: {user.Email}, Role: {user.Role}");

                // Check password - handle both plain text and BCrypt hash
                bool isPasswordValid = false;
                try
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
                    Console.WriteLine($"BCrypt verification: {isPasswordValid}");
                }
                catch (Exception bcryptEx)
                {
                    Console.WriteLine($"BCrypt error: {bcryptEx.Message}");
                    isPasswordValid = request.Password == user.PasswordHash;
                    Console.WriteLine($"Plain text verification: {isPasswordValid}");
                    if (isPasswordValid)
                    {
                        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                        await _context.SaveChangesAsync();
                        Console.WriteLine("Password updated to BCrypt hash");
                    }
                }
                if (!isPasswordValid)
                {
                    return BadRequest(new {
                        message = "Mật khẩu không đúng.",
                        passwordLength = request.Password.Length,
                        storedHashLength = user.PasswordHash?.Length ?? 0
                    });
                }

                // Get JWT settings with null checks
                var jwtSettings = _configuration.GetSection("Jwt");
                var jwtKey = jwtSettings["Key"];
                var jwtIssuer = jwtSettings["Issuer"];
                var jwtAudience = jwtSettings["Audience"];

                if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
                {
                    throw new InvalidOperationException("JWT configuration is missing or invalid");
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role ?? "customer")
                };

                var token = new JwtSecurityToken(
                    issuer: jwtIssuer,
                    audience: jwtAudience,
                    claims: claims,
                    expires: DateTime.Now.AddHours(2),
                    signingCredentials: creds
                );

                return Ok(new LoginResponse
                {
                    Token = new JwtSecurityTokenHandler().WriteToken(token),
                    Role = user.Role ?? "customer",
                    Message = "Đăng nhập thành công.",
                    Id = user.UserId.ToString()
                });
            }
            catch (Exception ex)
            {
                // Log the actual error for debugging
                Console.WriteLine($"Login error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                return StatusCode(500, new { 
                    message = "Đã xảy ra lỗi trong quá trình đăng nhập.", 
                    error = ex.Message 
                });
            }
        }

        // Debug endpoint để kiểm tra users trong database
        [HttpGet("debug/users")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _context.Users.Select(u => new {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Role,
                    HasPassword = !string.IsNullOrEmpty(u.PasswordHash)
                }).ToListAsync();

                return Ok(new { 
                    message = "Users retrieved successfully",
                    count = users.Count,
                    users = users
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    message = "Lỗi khi lấy danh sách users.", 
                    error = ex.Message 
                });
            }
        }

        // Debug endpoint để kiểm tra user cụ thể
        [HttpGet("debug/user/{email}")]
        public async Task<IActionResult> GetUserByEmail(string email)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                
                if (user == null)
                {
                    return NotFound(new { 
                        message = $"Không tìm thấy user với email: {email}",
                        searchedEmail = email
                    });
                }

                return Ok(new {
                    message = "User found",
                    user = new {
                        user.UserId,
                        user.Name,
                        user.Email,
                        user.Role,
                        PasswordHashLength = user.PasswordHash?.Length ?? 0,
                        HasPassword = !string.IsNullOrEmpty(user.PasswordHash)
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    message = "Lỗi khi tìm user.", 
                    error = ex.Message 
                });
            }
        }

    }
}
