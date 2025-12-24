using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechstoreBackend.Data;
using TechstoreBackend.Models;
using TechstoreBackend.Models.DTOs;
using System.IO;

namespace TechstoreBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public ChatController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/Chat/ask
        [HttpPost("ask")]
        [AllowAnonymous]
        public async Task<IActionResult> Ask([FromBody] ChatRequestDto req)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(req.Question))
            {
                return BadRequest(new { success = false, message = "Câu hỏi không hợp lệ" });
            }

            // Normalize Vietnamese query for better retrieval
            var normalizedQuestion = NormalizeVietnameseQuery(req.Question);

            int topK = req.TopK <= 0 ? 5 : Math.Min(req.TopK, 10);
            // Try semantic retrieval via Python microservice (Chroma)
            var svcResults = await QuerySearchServiceAsync(normalizedQuestion, topK);

            List<ChatProductSnippetDto> productSnippets;
            if (svcResults != null && svcResults.Count > 0)
            {
                // Map from search microservice response (prefer metadata fields)
                productSnippets = svcResults
                    .GroupBy(r => r.ProductId ?? -1)
                    .Select(g => g.First())
                    .Select(r => new ChatProductSnippetDto
                    {
                        ProductId = r.ProductId ?? 0,
                        Name = r.Name ?? string.Empty,
                        Brand = r.Brand ?? string.Empty,
                        Category = r.CategoryName,
                        Price = r.Price ?? 0,
                        Description = !string.IsNullOrWhiteSpace(r.Document) ? r.Document : null,
                        ImageUrl = NormalizeImageUrl(r.ImageUrl),
                        UseCase = string.IsNullOrWhiteSpace(r.UseCase) ? null : r.UseCase,
                        Usp = string.IsNullOrWhiteSpace(r.Usp) ? null : r.Usp,
                        SpecificationsText = string.IsNullOrWhiteSpace(r.SpecText) ? null : r.SpecText
                    })
                    .Take(topK)
                    .ToList();
            }
            else
            {
                // Fallback: simple keyword scoring on DB subset if service unavailable
                var q = normalizedQuestion.Trim();
                var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(t => t.ToLower()).Distinct().ToList();

                var pre = await _context.Products
                    .Select(p => new {
                        p.ProductId, p.Name, p.Brand, p.Description, p.Price, p.ImageUrl,
                        CategoryName = _context.Categorys.Where(c => c.CategoryId == p.CategoryId).Select(c => c.Name).FirstOrDefault()
                    })
                    .Take(300)
                    .ToListAsync();

                var ranked = pre
                    .Select(p => new {
                        p.ProductId,
                        p.Name,
                        p.Brand,
                        p.Description,
                        p.Price,
                        p.ImageUrl,
                        p.CategoryName,
                        Score = Score(p.Name, p.Brand, p.Description, terms)
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Price)
                    .Take(topK)
                    .ToList();

                productSnippets = ranked.Select(x => new ChatProductSnippetDto
                {
                    ProductId = x.ProductId,
                    Name = x.Name,
                    Brand = x.Brand,
                    Category = x.CategoryName,
                    Price = x.Price,
                    Description = x.Description,
                    ImageUrl = NormalizeImageUrl(x.ImageUrl)
                }).ToList();
            }

            var contextText = BuildContextText(productSnippets);

            var (answer, mode) = await TryCallOpenAIAsync(normalizedQuestion, contextText);
            if (string.IsNullOrWhiteSpace(answer))
            {
                // Fallback deterministic answer
                answer = BuildFallbackAnswer(normalizedQuestion, productSnippets);
                mode = "fallback";
            }

            var resp = new ChatResponseDto
            {
                Answer = answer,
                Products = productSnippets,
                Mode = mode
            };
            return Ok(new { success = true, message = "OK", data = resp });
        }

        private class SearchServiceResult
        {
            public string Id { get; set; } = string.Empty;
            public int? ProductId { get; set; }
            public int? ChunkIndex { get; set; }
            public double? Score { get; set; }
            public double? CrossScore { get; set; }
            public string? Name { get; set; }
            public string? Brand { get; set; }
            public string? CategoryName { get; set; }
            public decimal? Price { get; set; }
            public string? ImageUrl { get; set; }
            public string? Document { get; set; }
            [JsonPropertyName("use_case")] public string? UseCase { get; set; }
            [JsonPropertyName("usp")] public string? Usp { get; set; }
            [JsonPropertyName("spec_text")] public string? SpecText { get; set; }
        }

        private class SearchServiceResponse
        {
            public bool Success { get; set; }
            public List<SearchServiceResult> Results { get; set; } = new();
        }

        // --- Helpers ---
        private static string NormalizeImageUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return string.Empty;
            // Keep absolute URLs
            if (imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return imageUrl;
            // Extract filename and map to unified image API
            var fileName = Path.GetFileName(imageUrl);
            return $"http://localhost:5032/api/Product/image/{fileName}";
        }

        private static string NormalizeVietnameseQuery(string q)
        {
            q = q.ToLowerInvariant();
            q = q.Replace("pin trâu", "pin dung lượng lớn");
            q = q.Replace("chơi game", "hiệu năng cao");
            q = q.Replace("mượt", "hiệu năng ổn định");
            q = q.Replace("học online", "phục vụ học tập");
            q = q.Replace("giá rẻ", "phân khúc giá thấp");
            return q;
        }

        private async Task<List<SearchServiceResult>> QuerySearchServiceAsync(string question, int topK)
        {
            try
            {
                var url = Environment.GetEnvironmentVariable("SEARCH_SERVICE_URL") ?? "http://localhost:8000";
                using var http = new HttpClient();
                var payload = new { query = question, top_k = topK };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                using var contentJson = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync($"{url.TrimEnd('/')}/search", contentJson);
                if (!resp.IsSuccessStatusCode) return new List<SearchServiceResult>();
                var body = await resp.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<SearchServiceResponse>(body, _jsonOptions);
                return parsed != null && parsed.Success && parsed.Results != null ? parsed.Results : new List<SearchServiceResult>();
            }
            catch
            {
                return new List<SearchServiceResult>();
            }
        }

        private static int Score(string? name, string? brand, string? desc, List<string> terms)
        {
            int s = 0;
            foreach (var t in terms)
            {
                if (!string.IsNullOrEmpty(name) && name.Contains(t, StringComparison.OrdinalIgnoreCase)) s += 5;
                if (!string.IsNullOrEmpty(brand) && brand.Contains(t, StringComparison.OrdinalIgnoreCase)) s += 3;
                if (!string.IsNullOrEmpty(desc) && desc.Contains(t, StringComparison.OrdinalIgnoreCase)) s += 2;
            }
            return s;
        }

        private static string BuildContextText(List<ChatProductSnippetDto> prods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Dưới đây là danh sách sản phẩm liên quan (mô tả, mục đích sử dụng, điểm nổi bật, thông số):");
            int i = 1;
            foreach (var p in prods)
            {
                sb.AppendLine($"{i}. {p.Name} | Thương hiệu: {p.Brand} | Danh mục: {p.Category} | Giá: {p.Price:N0} VND");
                if (!string.IsNullOrWhiteSpace(p.UseCase)) sb.AppendLine($"   Mục đích sử dụng: {TrimField(p.UseCase, 300)}");
                if (!string.IsNullOrWhiteSpace(p.Usp)) sb.AppendLine($"   Điểm nổi bật: {TrimField(p.Usp, 300)}");
                if (!string.IsNullOrWhiteSpace(p.SpecificationsText)) sb.AppendLine($"   Thông số: {TrimField(p.SpecificationsText, 300)}");
                else if (!string.IsNullOrWhiteSpace(p.Description)) sb.AppendLine($"   Mô tả: {TrimField(p.Description, 300)}");
                i++;
            }
            return sb.ToString();
        }

        private static string TrimField(string? value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Length > maxLen ? value.Substring(0, maxLen) + "..." : value;
        }

        private static string BuildFallbackAnswer(string question, List<ChatProductSnippetDto> prods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Gợi ý sơ bộ dựa trên danh sách sản phẩm tìm được (chưa có câu trả lời từ mô hình AI):");
            foreach (var p in prods)
            {
                var highlight = p.Usp ?? p.UseCase ?? p.SpecificationsText ?? p.Description;
                if (!string.IsNullOrWhiteSpace(highlight))
                {
                    highlight = TrimField(highlight, 120);
                    sb.AppendLine($"- {p.Name} (~{p.Price:N0} VND): {highlight}");
                }
                else
                {
                    sb.AppendLine($"- {p.Name} (~{p.Price:N0} VND)");
                }
            }
            sb.AppendLine("Bạn có thể hỏi tiếp để lọc theo ngân sách, hiệu năng, pin hoặc nhu cầu cụ thể.");
            return sb.ToString().Trim();
        }

        private async Task<(string answer, string mode)> TryCallOpenAIAsync(string question, string context)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var model = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-4o-mini";
            if (string.IsNullOrWhiteSpace(apiKey)) return (string.Empty, "");

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                http.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

                var systemPrompt = @"
Bạn là trợ lý bán hàng công nghệ.
QUY TẮC BẮT BUỘC:
- Chỉ tư vấn các sản phẩm có trong Context
- KHÔNG tự suy đoán thông tin ngoài Context
- KHÔNG bịa giá, cấu hình, pin, hiệu năng
- Nếu Context không đủ thông tin → nói rõ là chưa có dữ liệu
- Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng
";
                var userPrompt = $"Câu hỏi: {question}\n\nContext:\n{context}\n\nYêu cầu: Tư vấn 3-5 sản phẩm phù hợp, nêu lý do ngắn gọn, dùng bullet và giá VNĐ. Chỉ dựa trên Context.";

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.2
                };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                using var contentJson = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", contentJson);
                if (!resp.IsSuccessStatusCode)
                {
                    return (string.Empty, "");
                }
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var choices = root.GetProperty("choices");
                if (choices.GetArrayLength() == 0) return (string.Empty, "");
                var msg = choices[0].GetProperty("message").GetProperty("content").GetString();
                return (msg ?? string.Empty, "openai");
            }
            catch
            {
                return (string.Empty, "");
            }
        }
    }
}
