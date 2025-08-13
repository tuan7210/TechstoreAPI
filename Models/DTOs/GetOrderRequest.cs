public class GetOrderRequest : BasePaginationRequest
{
    public string SearchText { get; set; } = string.Empty;
}