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

2. Implement a Dynamic Consumer Pool with a Maximum Limit (instead of serializing a shared consumer)
- **The Issue:** Serializing all operations on a single shared `Consumer` instance per cluster prevents true parallel fetching (Tab B blocks while Tab A fetches).
- **Alternative:** Implement a dynamic pool of consumers (e.g., `ConcurrentBag<IConsumer>`) with a maximum limit. 
    - The pool starts empty. When a tab requests a consumer, provide one if available.
    - If the pool is empty and the number of created consumers is below the maximum limit, instantiate a new consumer.
    - If the maximum limit is reached, the request waits asynchronously (`SemaphoreSlim.WaitAsync()`) until a consumer is returned.
    - This allows true concurrent fetching against the same cluster without thread-safety issues, while protecting against resource exhaustion.
- **Locking:** Where synchronization is still necessary (e.g., managing the pool itself or specific shared resources), explicitly favor `SemaphoreSlim.WaitAsync()` over the `lock` keyword to avoid thread-pool starvation or UI freezing during async network I/O.
- **Cancellation:** Ensure all concurrent operations accept a `CancellationToken` so that if a user closes a tab, the `SemaphoreSlim` or pooled consumer is released immediately.

3. Protect topic cache
- Replace `Dictionary` for topic metadata with `ConcurrentDictionary` to allow concurrent reads while safely handling updates.
- Ensure `ValidateTopic` and refresh paths use thread-safe update methods (like `AddOrUpdate` or `GetOrAdd`) to prevent unsafe interleaving.

4. Keep null-poll retry behavior
- Retain retry-on-timeout logic added in fetch loop to avoid false empty results from transient empty polls.

## Validation Targets
- Concurrent topic fetches on same cluster should not throw or deadlock.
- Concurrent topic + partition fetches should return stable results.
- No regression for single-tab latency and correctness.
- Fast cancellation: Closing a tab during a fetch should immediately release resources for other tabs without leaving orphaned locks.