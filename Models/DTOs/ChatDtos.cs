using System.ComponentModel.DataAnnotations;

namespace TechstoreBackend.Models.DTOs
{
    public class ChatRequestDto
    {
        [Required]
        public string Question { get; set; } = string.Empty;
        public int TopK { get; set; } = 5;
    }

    public class ChatProductSnippetDto
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string? Category { get; set; }
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class ChatResponseDto
    {
        public string Answer { get; set; } = string.Empty;
        public List<ChatProductSnippetDto> Products { get; set; } = new();
        public string Mode { get; set; } = "fallback"; // fallback | openai
    }
}
