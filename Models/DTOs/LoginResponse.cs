namespace TechstoreBackend.Models.DTOs
{
    public class LoginResponse
    {
        public string Token { get; set; }
        public string Role { get; set; }
        public string Message { get; set; }
        public string Id { get; set; }
    }
}
