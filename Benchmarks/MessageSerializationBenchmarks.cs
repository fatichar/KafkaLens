using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using KafkaLens.Shared.Models;

namespace Benchmarks;

/// <summary>
/// Measures the cost of binary serialization and deserialization of <see cref="Message"/>
/// objects at various payload sizes.  This is the hot path for saved-messages storage
/// (LocalClient) and any future persistence layer.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MessageSerializationBenchmarks
{
    private Message _smallMessage = null!;
    private Message _mediumMessage = null!;
    private Message _largeMessage = null!;
    private Message _messageWithHeaders = null!;
    private MemoryStream _serializedSmall = null!;
    private MemoryStream _serializedMedium = null!;
    private MemoryStream _serializedLarge = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new System.Random(42);

        _smallMessage  = MakeMessage(rng, keySize: 8, valueSize: 64);
        _mediumMessage = MakeMessage(rng, keySize: 16, valueSize: 1_024);
        _largeMessage  = MakeMessage(rng, keySize: 32, valueSize: 65_536);

        _messageWithHeaders = MakeMessage(rng, keySize: 16, valueSize: 512,
            headers: new Dictionary<string, byte[]>
            {
                ["correlation-id"] = rng.GetBytes(16),
                ["source-system"]  = rng.GetBytes(8),
                ["event-type"]     = rng.GetBytes(12),
            });

        _serializedSmall  = Serialize(_smallMessage);
        _serializedMedium = Serialize(_mediumMessage);
        _serializedLarge  = Serialize(_largeMessage);
    }

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Benchmark(Description = "Serialize – 64 B value")]
    public void Serialize_Small()
    {
        using var ms = new MemoryStream();
        _smallMessage.Serialize(ms);
    }

    [Benchmark(Description = "Serialize – 1 KB value")]
    public void Serialize_Medium()
    {
        using var ms = new MemoryStream();
        _mediumMessage.Serialize(ms);
    }

    [Benchmark(Description = "Serialize – 64 KB value")]
    public void Serialize_Large()
    {
        using var ms = new MemoryStream();
        _largeMessage.Serialize(ms);
    }

    [Benchmark(Description = "Serialize – 3 headers + 512 B value")]
    public void Serialize_WithHeaders()
    {
        using var ms = new MemoryStream();
        _messageWithHeaders.Serialize(ms);
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Benchmark(Description = "Deserialize – 64 B value")]
    public Message Deserialize_Small()
    {
        _serializedSmall.Position = 0;
        return Message.Deserialize(_serializedSmall);
    }

    [Benchmark(Description = "Deserialize – 1 KB value")]
    public Message Deserialize_Medium()
    {
        _serializedMedium.Position = 0;
        return Message.Deserialize(_serializedMedium);
    }

    [Benchmark(Description = "Deserialize – 64 KB value")]
    public Message Deserialize_Large()
    {
        _serializedLarge.Position = 0;
        return Message.Deserialize(_serializedLarge);
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Benchmark(Description = "Round-trip – 1 KB value")]
    public Message RoundTrip_Medium()
    {
        using var ms = new MemoryStream();
        _mediumMessage.Serialize(ms);
        ms.Position = 0;
        return Message.Deserialize(ms);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Message MakeMessage(
        System.Random rng, int keySize, int valueSize,
        Dictionary<string, byte[]>? headers = null)
    {
        return new Message(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            headers ?? new Dictionary<string, byte[]>(),
            rng.GetBytes(keySize),
            rng.GetBytes(valueSize))
        {
            Partition = 0,
            Offset = 42
        };
    }

    private static MemoryStream Serialize(Message msg)
    {
        var ms = new MemoryStream();
        msg.Serialize(ms);
        return ms;
    }
}

file static class RandomExtensions
{
    public static byte[] GetBytes(this System.Random rng, int count)
    {
        var buf = new byte[count];
        rng.NextBytes(buf);
        return buf;
    }
}
