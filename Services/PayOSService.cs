using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechstoreBackend.Services
{
    public class PayOSService
    {
        private readonly string _clientId;
        private readonly string _apiKey;
        private readonly string _checksumKey;
        private readonly HttpClient _httpClient;

        public PayOSService(IConfiguration config, HttpClient httpClient)
        {
            _clientId = config["PayOS:ClientId"];
            _apiKey = config["PayOS:ApiKey"];
            _checksumKey = config["PayOS:ChecksumKey"];
            _httpClient = httpClient;
        }

        // Hàm tạo Link thanh toán
        public async Task<object> CreatePaymentLink(long orderCode, int amount, string description, string returnUrl, string cancelUrl)
        {
            var url = "https://api-merchant.payos.vn/v2/payment-requests";

            // 1. Tạo dữ liệu body
            var requestData = new
            {
                orderCode = orderCode,
                amount = amount,
                description = description,
                buyerName = "Khach hang",
                buyerPhone = "0900000000",
                cancelUrl = cancelUrl,
                returnUrl = returnUrl,
                expiredAt = (long)(DateTime.UtcNow.AddMinutes(15) - new DateTime(1970, 1, 1)).TotalSeconds,
            };

            // 2. Tạo chữ ký (Signature) để bảo mật
            var signatureRaw = $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
            var signature = ComputeHmacSha256(signatureRaw, _checksumKey);

            // 3. Gép signature vào body
            var finalBody = new
            {
                orderCode,
                amount,
                description,
                buyerName = "Khach hang",
                buyerPhone = "0900000000",
                cancelUrl,
                returnUrl,
                signature = signature, // Quan trọng
                expiredAt = requestData.expiredAt
            };

            var jsonBody = JsonSerializer.Serialize(finalBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-client-id", _clientId);
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);

            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            // 5. Parse kết quả (SỬA ĐOẠN NÀY)
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (root.GetProperty("code").GetString() == "00")
            {
                var data = root.GetProperty("data");
                // Trả về object chứa đầy đủ thông tin để Client tự vẽ QR
                return new 
                {
                    bin = data.GetProperty("bin").GetString(),
                    accountNumber = data.GetProperty("accountNumber").GetString(),
                    accountName = data.GetProperty("accountName").GetString(),
                    amount = data.GetProperty("amount").GetInt32(),
                    description = data.GetProperty("description").GetString(),
                    orderCode = data.GetProperty("orderCode").GetInt64(),
                    checkoutUrl = data.GetProperty("checkoutUrl").GetString(),
                    qrCode = data.GetProperty("qrCode").GetString() // Chuỗi raw QR nếu muốn dùng thư viện
                };
            }

            throw new Exception($"PayOS Error: {root.GetProperty("desc").GetString()}");
    // a        
        }
        // Hàm tiện ích: Tính toán HMAC SHA256
        private string ComputeHmacSha256(string data, string key)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(data);
            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(messageBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}