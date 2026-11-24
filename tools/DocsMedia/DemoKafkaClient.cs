using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;

namespace DocsMedia;

/// <summary>
/// In-memory IKafkaLensClient serving deterministic, realistic-looking demo data
/// (a fictional e-commerce platform). Every message is derived from (topic, partition, offset),
/// so fetches are reproducible and honour position / count / partition like a real broker.
/// </summary>
public sealed class DemoKafkaClient(IClusterInfoRepository repo) : IKafkaLensClient
{
    public string Name => "Local";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => true;

    /// <summary>Reference "now" for all timestamps (wall clock, so the app's own log times line up).</summary>
    public static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private sealed record TopicSpec(string Name, int Partitions, long MessagesPerPartition, int IntervalMs, Func<Random, long, int, (string? key, byte[] value, Dictionary<string, byte[]> headers)> Make);

    private static readonly TopicSpec[] Topics =
    [
        new("orders.created", 6, 1_284_312, 1100, MakeOrder),
        new("orders.status-changed", 6, 2_910_455, 600, MakeOrderStatus),
        new("payments.transactions", 4, 978_201, 1500, MakePayment),
        new("inventory.stock-levels", 3, 412_870, 3000, MakeStock),
        new("shipments.tracking", 4, 1_530_118, 900, MakeShipment),
        new("customers.profile-updates", 3, 88_412, 9000, MakeCustomer),
        new("notifications.email", 2, 640_997, 2000, MakeEmail),
        new("checkout.cart-events", 8, 7_420_664, 150, MakeCart),
        new("metrics.api-latency-ms", 2, 12_840_211, 50, MakeLatency),
        new("audit.log", 1, 301_554, 4000, MakeAudit),
    ];

    private static readonly string[] StagingTopics = ["orders.created", "payments.transactions", "shipments.tracking", "customers.profile-updates"];

    public Task<bool> ValidateConnectionAsync(string bootstrapServers) => Task.FromResult(true);

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        var info = repo.Add(newCluster.Name, newCluster.Address);
        return Task.FromResult(new KafkaCluster(info.Id, info.Name, info.Address));
    }

    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync() =>
        Task.FromResult<IEnumerable<KafkaCluster>>(repo.GetAll().Values
            .Select(c => new KafkaCluster(c.Id, c.Name, c.Address) { IsEnabled = c.IsEnabled }).ToList());

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var c = repo.GetById(clusterId);
        return Task.FromResult(new KafkaCluster(c.Id, c.Name, c.Address));
    }

    public Task<KafkaCluster> GetClusterByNameAsync(string name)
    {
        var c = repo.GetAll().Values.First(x => x.Name == name);
        return Task.FromResult(new KafkaCluster(c.Id, c.Name, c.Address));
    }

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        var name = repo.GetById(clusterId).Name;
        var specs = name.Contains("staging", StringComparison.OrdinalIgnoreCase)
            ? Topics.Where(t => StagingTopics.Contains(t.Name))
            : Topics;
        return Task.FromResult<IList<Topic>>(specs.Select(t => new Topic(t.Name, t.Partitions)).ToList());
    }

    public Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        var c = repo.GetById(clusterId);
        c.Name = update.Name;
        c.Address = update.Address;
        repo.Update(c);
        return Task.FromResult(new KafkaCluster(c.Id, c.Name, c.Address));
    }

    public Task RemoveClusterByIdAsync(string clusterId)
    {
        repo.Delete(clusterId);
        return Task.CompletedTask;
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken ct = default)
        => Stream(() => Fetch(topic, null, options));

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken ct = default)
        => Stream(() => Fetch(topic, partition, options));

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken ct = default)
        => Task.FromResult(Fetch(topic, null, options));

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken ct = default)
        => Task.FromResult(Fetch(topic, partition, options));

    private static MessageStream Stream(Func<List<Message>> fetch)
    {
        var stream = new MessageStream();
        Dispatcher.UIThread.Post(() =>
        {
            stream.Messages.AddRange(fetch());
            stream.HasMore = false;
        });
        return stream;
    }

    // ── fetch semantics ────────────────────────────────────────────────────

    private static List<Message> Fetch(string topicName, int? partition, FetchOptions options)
    {
        var spec = Topics.First(t => t.Name == topicName);
        var partitions = partition is { } p ? [p] : Enumerable.Range(0, spec.Partitions).ToArray();
        var limit = Math.Max(1, options.Limit);
        var perPartition = (int)Math.Ceiling(limit / (double)partitions.Length);

        var result = new List<Message>();
        foreach (var part in partitions)
        {
            var hw = HighWatermark(spec, part);
            long from;
            if (options.Start.Type == PositionType.Timestamp)
                from = OffsetForTimestamp(spec, part, options.Start.Timestamp);
            else if (options.Start.Offset < 0) // End
                from = hw - perPartition;
            else
                from = options.Start.Offset;

            if (options.Direction == FetchDirection.Backward && options.Start.Type == PositionType.Timestamp)
                from -= perPartition;

            from = Math.Clamp(from, 0, hw);
            var to = Math.Min(hw, from + perPartition);
            for (var off = from; off < to; off++)
                result.Add(Build(spec, part, off));
        }

        return result.OrderBy(m => m.EpochMillis).TakeLast(limit).ToList();
    }

    private static long HighWatermark(TopicSpec spec, int partition) =>
        spec.MessagesPerPartition + partition * 7919L % 5000;

    private static long TimestampOf(TopicSpec spec, int partition, long offset)
    {
        var hw = HighWatermark(spec, partition);
        var jitter = (long)(Hash(spec.Name, partition, offset) % (ulong)spec.IntervalMs);
        return Now.ToUnixTimeMilliseconds() - (hw - offset) * spec.IntervalMs + jitter - 4_000;
    }

    private static long OffsetForTimestamp(TopicSpec spec, int partition, long ts)
    {
        var hw = HighWatermark(spec, partition);
        var delta = (Now.ToUnixTimeMilliseconds() - ts) / spec.IntervalMs;
        return hw - delta + 1;
    }

    private static Message Build(TopicSpec spec, int partition, long offset)
    {
        var rnd = new Random((int)(Hash(spec.Name, partition, offset) & 0x7fffffff));
        var ts = TimestampOf(spec, partition, offset);
        var (key, value, headers) = spec.Make(rnd, ts, partition);
        return new Message(ts, headers, key == null ? null : Encoding.UTF8.GetBytes(key), value)
        {
            Partition = partition,
            Offset = offset
        };
    }

    private static ulong Hash(string s, int p, long o)
    {
        ulong h = 1469598103934665603UL;
        foreach (var c in s) h = (h ^ c) * 1099511628211UL;
        h = (h ^ (ulong)p) * 1099511628211UL;
        h = (h ^ (ulong)o) * 1099511628211UL;
        return h ^ (h >> 29);
    }

    // ── payload factories ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly string[] FirstNames = ["Aarav", "Sofia", "Liam", "Mei", "Noah", "Amara", "Lucas", "Priya", "Elena", "Mateo", "Hana", "Oliver", "Zara", "Diego", "Ingrid", "Kenji", "Chloe", "Ravi", "Nadia", "Tomás"];
    private static readonly string[] LastNames = ["Sharma", "Rossi", "Müller", "Chen", "Okafor", "Silva", "Novak", "Tanaka", "García", "Johansson", "Patel", "Dubois", "Kowalski", "Haddad", "Larsen"];
    private static readonly string[] Cities = ["Berlin", "Amsterdam", "Lisbon", "Dublin", "Stockholm", "Madrid", "Milan", "Warsaw", "Vienna", "Copenhagen"];
    private static readonly string[] Countries = ["DE", "NL", "PT", "IE", "SE", "ES", "IT", "PL", "AT", "DK"];
    private static readonly (string sku, string name, decimal price)[] Products =
    [
        ("SKU-10482", "Wireless Noise-Cancelling Headphones", 189.00m),
        ("SKU-20931", "Mechanical Keyboard (Brown switches)", 129.90m),
        ("SKU-33017", "USB-C Docking Station", 149.50m),
        ("SKU-41126", "27\" 4K Monitor", 349.00m),
        ("SKU-52208", "Ergonomic Office Chair", 279.00m),
        ("SKU-60415", "Stainless Steel Water Bottle", 24.95m),
        ("SKU-71839", "Trail Running Shoes", 119.00m),
        ("SKU-80027", "Espresso Machine", 429.00m),
        ("SKU-91354", "Smart Fitness Watch", 199.00m),
        ("SKU-12760", "Organic Cotton Hoodie", 59.00m),
    ];
    private static readonly string[] Services = ["checkout-service", "order-service", "payment-gateway", "inventory-service", "shipping-service", "customer-service", "notification-service"];

    private static string Pick(Random r, string[] a) => a[r.Next(a.Length)];
    private static string Iso(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    private static string Uuid(Random r) { var b = new byte[16]; r.NextBytes(b); return new Guid(b).ToString(); }
    private static string Hex(Random r, int n) { var b = new byte[n]; r.NextBytes(b); return Convert.ToHexString(b).ToLowerInvariant(); }
    private static string OrderId(Random r) => $"ORD-2026-{r.Next(100000, 999999)}";
    private static string CustomerId(Random r) => $"CUST-{r.Next(10000, 99999)}";

    private static Dictionary<string, byte[]> Headers(Random r, string service, string schema, string contentType = "application/json") => new()
    {
        ["content-type"] = Encoding.UTF8.GetBytes(contentType),
        ["source-service"] = Encoding.UTF8.GetBytes(service),
        ["schema-version"] = Encoding.UTF8.GetBytes(schema),
        ["traceparent"] = Encoding.UTF8.GetBytes($"00-{Hex(r, 16)}-{Hex(r, 8)}-01"),
        ["correlation-id"] = Encoding.UTF8.GetBytes(Uuid(r)),
    };

    private static byte[] J(object o) => JsonSerializer.SerializeToUtf8Bytes(o, Json);

    private static object Customer(Random r)
    {
        var i = r.Next(Cities.Length);
        var first = Pick(r, FirstNames);
        var last = Pick(r, LastNames);
        return new
        {
            CustomerId = CustomerId(r),
            Name = $"{first} {last}",
            Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.com",
            Tier = r.Next(10) < 2 ? "gold" : r.Next(10) < 5 ? "silver" : "standard",
            ShippingAddress = new { City = Cities[i], Country = Countries[i], PostalCode = r.Next(10000, 99999).ToString() }
        };
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeOrder(Random r, long ts, int p)
    {
        var id = OrderId(r);
        var lines = Enumerable.Range(0, r.Next(1, 4)).Select(_ =>
        {
            var pr = Products[r.Next(Products.Length)];
            return new { Sku = pr.sku, Name = pr.name, Quantity = r.Next(1, 3), UnitPrice = pr.price };
        }).ToList();
        var total = lines.Sum(l => l.UnitPrice * l.Quantity);
        return (id, J(new
        {
            EventType = "OrderCreated",
            OrderId = id,
            CreatedAt = Iso(ts),
            Channel = r.Next(10) < 6 ? "web" : r.Next(2) == 0 ? "ios" : "android",
            Customer = Customer(r),
            Items = lines,
            Currency = "EUR",
            TotalAmount = total,
            Payment = new { Method = r.Next(10) < 6 ? "card" : r.Next(2) == 0 ? "paypal" : "klarna", Status = "AUTHORIZED" },
            Promotions = r.Next(4) == 0 ? new[] { "BLACKFRIDAY26" } : Array.Empty<string>()
        }), Headers(r, "order-service", "3"));
    }

    private static readonly string[] Statuses = ["CONFIRMED", "PICKING", "PACKED", "SHIPPED", "DELIVERED", "CANCELLED", "PAYMENT_FAILED"];

    private static (string?, byte[], Dictionary<string, byte[]>) MakeOrderStatus(Random r, long ts, int p)
    {
        var id = OrderId(r);
        var idx = r.Next(100) switch { < 20 => 0, < 38 => 1, < 54 => 2, < 74 => 3, < 92 => 4, < 96 => 5, _ => 6 };
        var to = Statuses[idx];
        return (id, J(new
        {
            EventType = "OrderStatusChanged",
            OrderId = id,
            FromStatus = idx switch { 0 or 6 => "CREATED", 5 => "CONFIRMED", _ => Statuses[idx - 1] },
            ToStatus = to,
            ChangedAt = Iso(ts),
            Reason = to switch { "CANCELLED" => "customer_request", "PAYMENT_FAILED" => "insufficient_funds", _ => (string?)null },
            Warehouse = $"WH-{Pick(r, ["BER", "AMS", "MAD", "WAW"])}-0{r.Next(1, 4)}"
        }), Headers(r, "order-service", "2"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakePayment(Random r, long ts, int p)
    {
        var failed = r.Next(100) < 7;
        var amount = Math.Round((decimal)(r.NextDouble() * 600 + 15), 2);
        return ($"txn_{Hex(r, 8)}", J(new
        {
            EventType = failed ? "PaymentDeclined" : "PaymentCaptured",
            TransactionId = $"txn_{Hex(r, 8)}",
            OrderId = OrderId(r),
            Amount = amount,
            Currency = "EUR",
            Method = new { Type = "card", Brand = Pick(r, ["visa", "mastercard", "amex"]), Last4 = r.Next(1000, 9999).ToString(), ThreeDSecure = true },
            Status = failed ? "DECLINED" : "CAPTURED",
            DeclineCode = failed ? Pick(r, ["insufficient_funds", "card_expired", "do_not_honor"]) : null,
            RiskScore = Math.Round(r.NextDouble() * (failed ? 0.9 : 0.3), 3),
            ProcessedAt = Iso(ts),
            Gateway = new { Provider = "stripe", LatencyMs = r.Next(80, 900) }
        }), Headers(r, "payment-gateway", "5"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeStock(Random r, long ts, int p)
    {
        var pr = Products[r.Next(Products.Length)];
        var available = r.Next(0, 480);
        return (pr.sku, J(new
        {
            Sku = pr.sku,
            ProductName = pr.name,
            Warehouse = $"WH-{Pick(r, ["BER", "AMS", "MAD", "WAW"])}-0{r.Next(1, 4)}",
            Available = available,
            Reserved = r.Next(0, 40),
            ReorderPoint = 50,
            LowStock = available < 50,
            UpdatedAt = Iso(ts)
        }), Headers(r, "inventory-service", "1"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeShipment(Random r, long ts, int p)
    {
        var i = r.Next(Cities.Length);
        var ev = Pick(r, ["LABEL_CREATED", "PICKED_UP", "IN_TRANSIT", "ARRIVED_AT_HUB", "OUT_FOR_DELIVERY", "DELIVERED"]);
        return ($"SHP-{r.Next(1000000, 9999999)}", J(new
        {
            ShipmentId = $"SHP-{r.Next(1000000, 9999999)}",
            OrderId = OrderId(r),
            Carrier = Pick(r, ["DHL", "UPS", "PostNL", "DPD", "GLS"]),
            TrackingNumber = $"{r.Next(100000000, 999999999)}{r.Next(100, 999)}",
            Event = ev,
            Location = new { City = Cities[i], Country = Countries[i], Lat = Math.Round(40 + r.NextDouble() * 20, 4), Lon = Math.Round(-8 + r.NextDouble() * 30, 4) },
            EstimatedDelivery = DateTimeOffset.FromUnixTimeMilliseconds(ts).AddDays(r.Next(1, 4)).ToString("yyyy-MM-dd"),
            OccurredAt = Iso(ts)
        }), Headers(r, "shipping-service", "2"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeCustomer(Random r, long ts, int p)
    {
        var c = Customer(r);
        return (CustomerId(r), J(new
        {
            EventType = "CustomerProfileUpdated",
            Customer = c,
            ChangedFields = new[] { Pick(r, ["email", "shipping_address", "tier", "marketing_opt_in"]) },
            MarketingOptIn = r.Next(2) == 0,
            UpdatedBy = r.Next(4) == 0 ? "support-agent" : "self",
            UpdatedAt = Iso(ts)
        }), Headers(r, "customer-service", "4"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeEmail(Random r, long ts, int p)
    {
        var first = Pick(r, FirstNames).ToLowerInvariant();
        return (Uuid(r), J(new
        {
            MessageId = Uuid(r),
            Template = Pick(r, ["order_confirmation", "shipping_update", "password_reset", "abandoned_cart", "delivery_confirmation"]),
            Recipient = $"{first}@example.com",
            Locale = Pick(r, ["en-GB", "de-DE", "nl-NL", "es-ES"]),
            Status = r.Next(30) == 0 ? "BOUNCED" : "SENT",
            Provider = "ses",
            SentAt = Iso(ts)
        }), Headers(r, "notification-service", "1"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeCart(Random r, long ts, int p)
    {
        var pr = Products[r.Next(Products.Length)];
        return ($"sess_{Hex(r, 6)}", J(new
        {
            EventType = Pick(r, ["ItemAdded", "ItemAdded", "ItemRemoved", "CartViewed", "CheckoutStarted"]),
            SessionId = $"sess_{Hex(r, 6)}",
            CustomerId = r.Next(3) == 0 ? null : CustomerId(r),
            Sku = pr.sku,
            Quantity = r.Next(1, 3),
            Price = pr.price,
            Device = new { Type = Pick(r, ["desktop", "mobile", "tablet"]), Os = Pick(r, ["Windows", "macOS", "iOS", "Android", "Linux"]) },
            Timestamp = Iso(ts)
        }), Headers(r, "checkout-service", "2"));
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeLatency(Random r, long ts, int p)
    {
        // Binary payload: big-endian int64 latency in microseconds — shows off numeric formatters.
        var micros = (long)(r.NextDouble() < 0.97 ? r.Next(800, 45_000) : r.Next(120_000, 900_000));
        var bytes = BitConverter.GetBytes(micros);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        var endpoint = Pick(r, ["GET /api/orders", "POST /api/checkout", "GET /api/products", "POST /api/payments", "GET /api/cart"]);
        return (endpoint, bytes, new Dictionary<string, byte[]>
        {
            ["content-type"] = "application/octet-stream"u8.ToArray(),
            ["unit"] = "microseconds"u8.ToArray(),
            ["host"] = Encoding.UTF8.GetBytes($"api-{r.Next(1, 9)}.eu-west-1.internal"),
        });
    }

    private static (string?, byte[], Dictionary<string, byte[]>) MakeAudit(Random r, long ts, int p)
    {
        var level = r.Next(20) switch { 0 => "ERROR", < 4 => "WARN", _ => "INFO" };
        var svc = Pick(r, Services);
        var msg = level switch
        {
            "ERROR" => Pick(r, ["Timeout calling payment-gateway after 3 retries", "Failed to reserve stock: optimistic lock conflict", "Upstream returned 503 Service Unavailable"]),
            "WARN" => Pick(r, ["Slow query detected (1.8s) on orders_by_customer", "Retrying message delivery (attempt 2/5)", "Rate limit at 85% for partner API"]),
            _ => Pick(r, ["Order state transition persisted", "Cache refreshed for product catalog", "Consumer group rebalanced", "Webhook delivered to partner endpoint"]),
        };
        var text = $"{Iso(ts)} {level,-5} [{svc}] {msg} trace={Hex(r, 8)}";
        return (svc, Encoding.UTF8.GetBytes(text), new Dictionary<string, byte[]>
        {
            ["content-type"] = "text/plain"u8.ToArray(),
            ["source-service"] = Encoding.UTF8.GetBytes(svc),
        });
    }
}
