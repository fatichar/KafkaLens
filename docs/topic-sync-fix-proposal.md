# Proposal: open tab stays empty after cluster reconnects

Status: **proposal, no code changed.** Pick one option before implementation.

## Problem

1. Open two gRPC clusters while the VPN is down. Both tabs show Disconnected.
2. Connect the VPN. Cluster status turns Connected (green).
3. Open tabs show no topics.
4. Closing and reopening the tab shows topics.

## Root cause (from code reading; not yet reproduced)

- `ClusterViewModel` owns the authoritative topic list and `TopicLoadState`.
- `OpenedClusterViewModel` keeps its own `Topics` collection, filled only by `SyncTopics()`.
- `SyncTopics()` is called from:
  - `OpenedClusterViewModel.LoadTopicsAsync()` (`ViewModels/OpenedClusterViewModel.Topics.cs`). It returns early on `Failed` and never syncs afterwards.
  - The `TopicLoadState == Loaded` handler in `OnClusterPropertyChanged` (`ViewModels/OpenedClusterViewModel.cs`). It is skipped while `isSyncingTopics` is true.
- After recovery, the cluster reaches `Loaded` through the periodic health check (`ClusterViewModel.CheckConnectionAsync` → `EnsureTopicsLoadedAsync`). If that happens while the tab's own load is still in flight, the sync is dropped and nothing retries it.
- Reopening works because `LoadTopicsAsync()` short-circuits on `Loaded` and calls `SyncTopics()`. The cluster already holds the topics; only the tab's copy is stale.

## Options

### Option 1: Remove the `!isSyncingTopics` guard

Change the condition at `OpenedClusterViewModel.cs` (`OnClusterPropertyChanged`) to sync on every `TopicLoadState == Loaded` transition.

- Pros: one-line change; `SyncTopics()` is idempotent (clear and rebuild).
- Cons: may run `SyncTopics()` twice during a load. Also re-runs `RestorePendingSessionState()`, which costs extra work on large topic lists. Does not explain why the guard exists.
- Risk: low. Test: a unit test that raises `TopicLoadState = Loaded` while `LoadTopicsAsync` is in flight and asserts `Topics` is populated.

### Option 2: Tab waits for a terminal state and syncs on a later `Loaded` (recommended)

- `LoadTopicsAsync()` waits until `TopicLoadState` is `Loaded` or `Failed`.
- On `Failed`, the tab sets a pending-sync flag and stays subscribed. When the cluster later reaches `Loaded`, the tab syncs once and clears the flag.
- Subscription is cleaned up in `Dispose()` and `ReattachCluster()`.
- Pros: explicit; does not depend on event ordering; covers every reconnect path.
- Cons: more code in the tab (flag plus subscription lifecycle). Needs care with `ReattachCluster`.
- Risk: medium. Tests: failed-then-recovered load populates `Topics`; `Dispose` unsubscribes; reattach while pending.

### Option 3: Sync on every connection-recovery transition

- In addition to `TopicLoadState`, the tab also syncs when `Status` transitions to `Connected`.
- Pros: covers recovery even when the topic load was already `Loaded` before the tab subscribed.
- Cons: adds a second trigger that can fire without a real topic change; more events to reason about.
- Risk: medium. Test: `Status` → `Connected` with unchanged topics does not clear the tab's selection state.

## Recommendation

Option 2. It fixes the missing trigger directly. Option 1 is an acceptable quick fix if minimal change is preferred. Option 3 is a fallback and not needed if option 2 is chosen.

## Impacted components

- `ViewModels/OpenedClusterViewModel.cs`, `ViewModels/OpenedClusterViewModel.Topics.cs`
- `ViewModels/ClusterViewModel.cs` (no change expected for options 1 and 2)
- `ViewModels.Tests/OpenedClusterViewModelBusinessLogicTests.cs` (regression tests)

## Open questions

- Confirm the reproduction path with a live VPN drop and reconnect before choosing. The fix should be validated against the exact interleaving, not only the inferred one.
