using System.Collections.Generic;

namespace TechstoreBackend.Models.DTOs
{
    public class CartDto
    {
        public int CartId { get; set; }
        public int UserId { get; set; }
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
    }

    // CartItemDto is already defined elsewhere. Remove duplicate definition.
}