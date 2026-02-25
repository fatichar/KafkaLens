# Concurrent Fetch Plan (Same Cluster, Multiple Tabs)

## Context
Users can open the same cluster in multiple tabs and trigger fetches concurrently (different topics/partitions).

## Current Behavior
- `SharedClient` keeps one `IKafkaConsumer` per cluster in a plain `Dictionary`.
- Topic fetch (`GetMessagesAsync(topic, ...)`) uses parallel per-partition consumers for consume calls.
- Partition fetch and parts of topic fetch setup still use shared consumer state.

## Known Risks
1. Consumer cache race
- `Dictionary` access in `SharedClient` is not thread-safe for concurrent reads/writes.

2. Shared consumer race
- `Confluent.Kafka` consumer is not thread-safe.
- Some calls are locked, but not all shared-consumer operations are serialized (e.g., `OffsetsForTimes`).

3. Topic metadata cache race
- Topic load/update path (`Topics` dictionary) is unsynchronized and can race under concurrent fetches.

## Proposed Fix (for later implementation)
1. Make consumer cache concurrency-safe
- Replace `Dictionary<string, IKafkaConsumer>` with `ConcurrentDictionary<string, IKafkaConsumer>`.
- Use `GetOrAdd` for creation paths.

2. Serialize shared-consumer operations
- Add a per-`ConfluentConsumer` lock or `SemaphoreSlim`.
- Protect all operations touching the shared `Consumer` instance (assign/consume/unassign, offsets-for-times, dispose path).

3. Protect topic cache
- Guard topic refresh/load and `Topics` mutations with a lock.
- Ensure `ValidateTopic` and refresh cannot interleave unsafely.

4. Keep null-poll retry behavior
- Retain retry-on-timeout logic added in fetch loop to avoid false empty results from transient empty polls.

## Validation Targets
- Concurrent topic fetches on same cluster should not throw or deadlock.
- Concurrent topic + partition fetches should return stable results.
- No regression for single-tab latency and correctness.
