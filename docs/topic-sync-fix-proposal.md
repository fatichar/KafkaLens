# Proposal: open tab stays empty after cluster reconnects

Status: **Option A1 implemented** (Option A plus the follow-up-load skip below).

## Problem

1. Start the app with the VPN down. gRPC clients are unreachable.
2. Open a tab on a gRPC cluster. It shows Disconnected.
3. Connect the VPN. The cluster turns Connected (green).
4. The open tab shows no topics. Closing and reopening the tab shows them.

## Evidence (user reproduction log, abridged)

```
18:37:58 [ERR] Failed to connect to grpc server: http://...-sandbox-...
18:37:58 [INF] Loaded 1 clusters from Dev                 <- placeholder cluster
18:38:20 [INF] Opening cluster: Dev                       <- tab opened on placeholder
18:38:21 [INF] Loading clusters for client: Dev           <- RefreshClustersForClientAsync, still failing
18:38:27 [INF] Periodic connection check ... Lab / Integ  <- placeholders skipped by health check
18:38:30 [INF] Loading clusters for client: Dev ...       <- DiscoverClientsNeedingRefreshAsync, fails
18:39:01 [INF] Loaded 1 clusters from Dev                 <- VPN up: real cluster discovered
18:39:27 [INF] Periodic connection check ... Sandbox ...  <- real cluster checked, turns green
          (no "Loading topics" for any gRPC cluster)
18:39:28 [INF] Loading topics... / Loaded 125 topics      <- Integ (local client) recovering via topicsError path
```

The same-instance recovery path works (Integ). The gRPC tabs never request topics.

## Root cause

1. At startup each unreachable gRPC client produces an `IsUnavailablePlaceholder` cluster. The tab opens on it.
2. `EnsureTopicsLoadedAsync()` returns immediately for placeholders, so no topic load and no `topicsError` is recorded.
3. When the VPN returns, periodic discovery (`MainViewModel.DiscoverClientsNeedingRefreshAsync`) replaces the placeholder with the real cluster. `ReattachOrphanedTabs` re-points the tab via `OpenedClusterViewModel.ReattachCluster`.
4. `ReattachCluster` only calls `SyncTopics()`. The new cluster is `NotLoaded`, so this does nothing.
5. The follow-up `ClusterViewModel.CheckConnectionAsync` loads topics only when `topicsError != null`. The new cluster has never failed a topic load, so it turns green with `TopicLoadState == NotLoaded`.
6. Nothing else starts a load. Reopening the tab works because `AddTab` calls `LoadTopicsAsync()`.

The on-demand path `RefreshClustersForClientAsync` does call `opened.LoadTopicsAsync()` after reattaching. Periodic discovery does not. That asymmetry is the gap.

The previous revision blamed the `!isSyncingTopics` guard. That was wrong: the guard only suppresses a duplicate sync while the tab's own awaited load is in flight, and removing it would break saved-tab selection restore.

## Options

### Option A: start a topic load in `ReattachCluster` (recommended)

Replace `SyncTopics()` at the end of `ReattachCluster` with `_ = LoadTopicsAsync();`, the same pattern `AddTab` uses.

- Pros: one place; covers every reattach path (periodic discovery and on-demand). If the load fails, `topicsError` is set and the existing health-check retry takes over. No change to locking or health-check semantics.
- Cons: fire-and-forget from a sync method. On its own it double-loads on the on-demand path (see A1).
- Risk: low.

#### A1: Option A plus skipping already-loaded tabs (chosen)

Option A alone double-loads in `RefreshClustersForClientAsync` (found in review). If the reattached tab's load finishes before the connection check returns, the follow-up `opened.LoadTopicsAsync()` runs with populated topics. It passes `forceRefresh: Topics.Count > 0`, so it starts a second fetch rather than joining the completed task. That rebuild discards the tab's nodes, selection, and restored session state.

A1 keeps Option A and makes the follow-up loop in `RefreshClustersForClientAsync` skip tabs whose cluster is already `Loaded` or `Loading`. The loop still covers tabs on existing clusters whose topics were reset (for example, after an address change).

### Option B: make periodic discovery match the on-demand path

After the checks in `DiscoverClientsNeedingRefreshAsync`, call `LoadTopicsAsync()` for tabs on the checked clusters, via a helper shared with `RefreshClustersForClientAsync`.

- Pros: orchestration stays in `MainViewModel`, consistent with existing code.
- Cons: covers only these two flows; any future reattach path must remember to do the same. Awaiting the loads holds `clusterRefreshLock`: a failing load with retries (~15s) would skip health checks, so loads must be fire-and-forget.
- Risk: medium.

### Option C: load topics on every successful connection check while not loaded

In `CheckConnectionAsync`, load when `isConnected && TopicLoadState != Loaded` instead of `topicsError != null`.

- Pros: cluster-level guarantee that a connected cluster has topics.
- Cons: fetches topic metadata for every cluster at startup and after each recovery, including clusters with no open tab. Costly with many large clusters and changes health-check semantics.
- Risk: medium, performance.

## Tests

Added to `ViewModels.Tests/MainViewModelBusinessLogicTests.cs`:

- `PeriodicDiscoveryReplacingPlaceholder_ShouldLoadTopicsForReattachedTab`: the reported bug.
- `OnDemandRefresh_TopicLoadFinishesBeforeCheck_ShouldNotReloadReattachedTab`: one fetch, and the tab's topic nodes keep their instances.
- `OnDemandRefresh_TopicLoadFinishesAfterCheck_ShouldLoadOnce`: the in-flight load populates the tab, with no second fetch.

All three fail without the fix. The second also fails with Option A alone.

## Impacted components (A1)

- `ViewModels/OpenedClusterViewModel.cs` (`ReattachCluster`)
- `ViewModels/MainViewModel.ClusterLoading.cs` (`RefreshClustersForClientAsync`)
- `ViewModels.Tests/MainViewModelBusinessLogicTests.cs`
