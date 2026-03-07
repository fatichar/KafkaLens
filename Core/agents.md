# Core — Agent Notes

See root [`agents.md`](../agents.md) for project-wide conventions.

## Layer Role

Core contains the Kafka consumer infrastructure used by `LocalClient` and the server-side `GrpcApi`.
It has **no dependency on UI layers**.

## Key Classes

| Class | Purpose |
|---|---|
| `SharedClient` | Multi-cluster Kafka client; consumer cache per cluster |
| `ConsumerFactory` | Creates `IKafkaConsumer` instances |
| `ConfluentConsumer` | Wraps Confluent.Kafka `IConsumer<>` |
| `ConsumerBase` | Abstract base with fetch/offset logic |
| `WatermarkHelper` | Partition watermark offset utilities |
| `MessageConverter` | Converts raw Kafka messages to domain models |

## Concurrency Rules

- `SharedClient` uses `ConcurrentDictionary<string, IKafkaConsumer>` for the consumer cache.
- `Confluent.Kafka` `IConsumer<>` is **not thread-safe** — do not call it from multiple threads simultaneously.
- Prefer `SemaphoreSlim.WaitAsync()` over `lock` for async-safe synchronization.
- All fetch operations must accept and honour `CancellationToken`.

## Known Tech Debt

See [`docs/concurrent-fetch-plan.md`](../docs/concurrent-fetch-plan.md) for the planned consumer
pool refactor (concurrent fetches across tabs on the same cluster).

Do not add new locking patterns that conflict with that plan.

## Testing

Test project: `Core.Tests/`

- Tests must not require a live Kafka broker.
- Use fakes/mocks for `IKafkaConsumer`.
- Test watermark logic, message conversion, and offset calculations in isolation.
