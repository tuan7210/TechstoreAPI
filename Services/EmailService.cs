using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TechstoreBackend.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
        {
            try
            {
                var emailSettings = _configuration.GetSection("EmailSettings");
                
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(emailSettings["SenderName"], emailSettings["SenderEmail"]));
                message.To.Add(new MailboxAddress("", to));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                if (isHtml)
                {
                    bodyBuilder.HtmlBody = body;
                }
                else
                {
                    bodyBuilder.TextBody = body;
                }

                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                
                await client.ConnectAsync(
                    emailSettings["SmtpServer"],
                    int.Parse(emailSettings["SmtpPort"]),
                    SecureSocketOptions.StartTls);

                // Chỉ xác thực nếu cần
                if (!string.IsNullOrEmpty(emailSettings["SmtpUsername"]) && !string.IsNullOrEmpty(emailSettings["SmtpPassword"]))
                {
                    await client.AuthenticateAsync(emailSettings["SmtpUsername"], emailSettings["SmtpPassword"]);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation($"Email sent successfully to {to}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send email: {ex.Message}");
                return false;
            }
        }

        public string GenerateVerificationCode()
        {
            // Tạo mã xác thực 6 chữ số ngẫu nhiên
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        public string GenerateVerificationEmailBody(string name, string verificationCode)
        {
            return $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #ddd; border-radius: 5px; }}
                    .header {{ background-color: #4CAF50; color: white; padding: 10px; text-align: center; border-radius: 5px 5px 0 0; }}
                    .content {{ padding: 20px; }}
                    .verification-code {{ font-size: 24px; font-weight: bold; text-align: center; margin: 20px 0; padding: 10px; background-color: #f5f5f5; border-radius: 5px; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #777; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h2>Xác nhận Email</h2>
                    </div>
                    <div class='content'>
                        <p>Xin chào {name},</p>
                        <p>Cảm ơn bạn đã đăng ký tài khoản tại TechStore. Để hoàn tất quá trình đăng ký, vui lòng nhập mã xác thực sau:</p>
                        <div class='verification-code'>{verificationCode}</div>
                        <p>Mã xác thực này sẽ hết hạn sau 10 phút. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
                        <p>Nếu bạn không thực hiện yêu cầu này, bạn có thể bỏ qua email này.</p>
                        <p>Trân trọng,<br>Đội ngũ TechStore</p>
                    </div>
                    <div class='footer'>
                        <p>Email này được gửi tự động, vui lòng không trả lời.</p>
                    </div>
                </div>
            </body>
            </html>";
        }
    }

    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true);
        string GenerateVerificationCode();
        string GenerateVerificationEmailBody(string name, string verificationCode);
    }
}
