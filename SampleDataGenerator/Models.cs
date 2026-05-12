using System.Text.Json.Serialization;

namespace SampleDataGenerator;

public record UserMessage
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;
    
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }
    
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

public record OrderMessage
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;
    
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;
    
    [JsonPropertyName("product_id")]
    public string ProductId { get; init; } = string.Empty;
    
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
    
    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; init; }
    
    [JsonPropertyName("order_date")]
    public DateTime OrderDate { get; init; }
    
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

public record ProductMessage
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; init; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;
    
    [JsonPropertyName("price")]
    public decimal Price { get; init; }
    
    [JsonPropertyName("stock")]
    public int Stock { get; init; }
    
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; init; }
}

public record DeliveryMessage
{
    [JsonPropertyName("delivery_id")]
    public string DeliveryId { get; init; } = string.Empty;
    
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;
    
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
    
    [JsonPropertyName("estimated_delivery")]
    public DateTime EstimatedDelivery { get; init; }
    
    [JsonPropertyName("shipped_at")]
    public DateTime? ShippedAt { get; init; }
}
