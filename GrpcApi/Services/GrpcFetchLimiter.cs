using GrpcApi.Config;

namespace GrpcApi.Services;

public sealed class GrpcFetchLimiter : IDisposable
{
    private readonly SemaphoreSlim semaphore;

    public GrpcFetchLimiter(GrpcFetchConfig config)
    {
        var maxConcurrentFetches = Math.Max(1, config.MaxConcurrentFetches);
        semaphore = new SemaphoreSlim(maxConcurrentFetches, maxConcurrentFetches);
    }

    public async Task<bool> TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await semaphore.WaitAsync(timeout, cancellationToken);
    }

    public void Release()
    {
        semaphore.Release();
    }

    public void Dispose()
    {
        semaphore.Dispose();
    }
}
