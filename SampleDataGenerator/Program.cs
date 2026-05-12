using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace SampleDataGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        var config = LoadConfiguration();
        var adminConfig = new AdminClientConfig { BootstrapServers = config.Kafka.BootstrapServers };
        
        Console.WriteLine("Creating topics if they don't exist...");
        await CreateTopicsAsync(adminConfig, config.Topics);
        
        Console.WriteLine("Starting message generation...");
        var producerConfig = new ProducerConfig { BootstrapServers = config.Kafka.BootstrapServers };
        
        var tasks = config.Topics.Names.Select(topicName => 
            PublishMessagesAsync(producerConfig, topicName, config)
        ).ToArray();
        
        await Task.WhenAll(tasks);
        Console.WriteLine("All messages published successfully!");
    }

    static async Task CreateTopicsAsync(AdminClientConfig adminConfig, TopicsConfig topicsConfig)
    {
        using var adminClient = new AdminClientBuilder(adminConfig).Build();
        
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var existingTopics = metadata.Topics.Select(t => t.Topic).ToHashSet();
        
        var topicsToCreate = topicsConfig.Names
            .Where(name => !existingTopics.Contains(name))
            .Select(name => new TopicSpecification
            {
                Name = name,
                NumPartitions = topicsConfig.NumPartitions,
                ReplicationFactor = topicsConfig.ReplicationFactor
            }).ToList();
        
        if (topicsToCreate.Any())
        {
            await adminClient.CreateTopicsAsync(topicsToCreate);
            Console.WriteLine($"Created topics: {string.Join(", ", topicsToCreate.Select(t => t.Name))}");
        }
        else
        {
            Console.WriteLine("All topics already exist.");
        }
    }

    static async Task PublishMessagesAsync(ProducerConfig producerConfig, string topicName, AppConfig config)
    {
        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        var random = new Random();
        
        for (int i = 0; i < config.Topics.MessagesPerTopic; i++)
        {
            var message = topicName switch
            {
                "User" => GenerateUserMessage(random),
                "Order" => GenerateOrderMessage(random),
                "Product" => GenerateProductMessage(random),
                "Delivery" => GenerateDeliveryMessage(random),
                _ => throw new ArgumentException($"Unknown topic: {topicName}")
            };
            
            var key = message switch
            {
                UserMessage m => m.UserId,
                OrderMessage m => m.OrderId,
                ProductMessage m => m.ProductId,
                DeliveryMessage m => m.DeliveryId,
                _ => Guid.NewGuid().ToString()
            };
            
            var json = JsonSerializer.Serialize(message);
            await producer.ProduceAsync(topicName, new Message<string, string> { Key = key, Value = json });
            
            if (i % 100 == 0)
            {
                Console.WriteLine($"[{topicName}] Published {i}/{config.Topics.MessagesPerTopic} messages");
            }
            
            // Random delay to simulate real-life timing
            var delay = random.Next(config.Publishing.MinDelayMs, config.Publishing.MaxDelayMs);
            await Task.Delay(delay);
        }
        
        producer.Flush(TimeSpan.FromSeconds(10));
        Console.WriteLine($"[{topicName}] Completed publishing {config.Topics.MessagesPerTopic} messages");
    }

    static UserMessage GenerateUserMessage(Random random)
    {
        var firstNames = new[] { "John", "Jane", "Mike", "Sarah", "David", "Emily", "Chris", "Emma", "Robert", "Lisa" };
        var lastNames = new[] { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Wilson", "Taylor" };
        var statuses = new[] { "active", "inactive", "pending", "suspended" };
        
        return new UserMessage
        {
            UserId = Guid.NewGuid().ToString(),
            Name = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}",
            Email = $"user{random.Next(10000)}@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-random.Next(365)),
            Status = statuses[random.Next(statuses.Length)]
        };
    }

    static OrderMessage GenerateOrderMessage(Random random)
    {
        var statuses = new[] { "pending", "confirmed", "shipped", "delivered", "cancelled" };
        
        return new OrderMessage
        {
            OrderId = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            ProductId = Guid.NewGuid().ToString(),
            Quantity = random.Next(1, 10),
            TotalAmount = random.Next(10, 1000) + (decimal)random.NextDouble(),
            OrderDate = DateTime.UtcNow.AddDays(-random.Next(30)),
            Status = statuses[random.Next(statuses.Length)]
        };
    }

    static ProductMessage GenerateProductMessage(Random random)
    {
        var categories = new[] { "Electronics", "Clothing", "Books", "Home", "Sports", "Toys" };
        var productNames = new[] 
        { 
            "Wireless Headphones", "Running Shoes", "Science Fiction Novel", "Coffee Maker", 
            "Yoga Mat", "Board Game", "Smart Watch", "T-Shirt", "Cookbook", "Desk Lamp"
        };
        
        return new ProductMessage
        {
            ProductId = Guid.NewGuid().ToString(),
            Name = productNames[random.Next(productNames.Length)],
            Category = categories[random.Next(categories.Length)],
            Price = random.Next(10, 500) + (decimal)random.NextDouble(),
            Stock = random.Next(0, 100),
            UpdatedAt = DateTime.UtcNow.AddDays(-random.Next(7))
        };
    }

    static DeliveryMessage GenerateDeliveryMessage(Random random)
    {
        var statuses = new[] { "processing", "in_transit", "delivered", "failed" };
        var addresses = new[]
        {
            "123 Main St, New York, NY",
            "456 Oak Ave, Los Angeles, CA",
            "789 Pine Rd, Chicago, IL",
            "321 Elm Blvd, Houston, TX",
            "654 Maple Dr, Phoenix, AZ"
        };
        
        var shippedAt = random.Next(0, 2) == 1 ? DateTime.UtcNow.AddDays(-random.Next(5)) : (DateTime?)null;
        
        return new DeliveryMessage
        {
            DeliveryId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid().ToString(),
            Address = addresses[random.Next(addresses.Length)],
            Status = statuses[random.Next(statuses.Length)],
            EstimatedDelivery = DateTime.UtcNow.AddDays(random.Next(1, 10)),
            ShippedAt = shippedAt
        };
    }

    static AppConfig LoadConfiguration()
    {
        var json = File.ReadAllText("appsettings.json");
        return JsonSerializer.Deserialize<AppConfig>(json) 
            ?? throw new InvalidOperationException("Failed to load configuration");
    }
}

public record AppConfig
{
    public KafkaConfig Kafka { get; init; } = null!;
    public TopicsConfig Topics { get; init; } = null!;
    public PublishingConfig Publishing { get; init; } = null!;
}

public record KafkaConfig
{
    public string BootstrapServers { get; init; } = string.Empty;
}

public record TopicsConfig
{
    public List<string> Names { get; init; } = new();
    public int MessagesPerTopic { get; init; }
    public int ReplicationFactor { get; init; }
    public int NumPartitions { get; init; }
}

public record PublishingConfig
{
    public int MinDelayMs { get; init; }
    public int MaxDelayMs { get; init; }
}
