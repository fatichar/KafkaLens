using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks.Infrastructure;

/// <summary>
/// Manages a single Avalonia headless application instance running on a dedicated
/// background thread.  ViewModel-level benchmarks use this session to post work to
/// the Avalonia UI thread and await results.
/// </summary>
public sealed class HeadlessSession : IDisposable
{
    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private bool _disposed;

    private HeadlessSession() { }

    public IServiceProvider Services =>
        ((BenchmarkApp)Application.Current!).Services;

    /// <summary>
    /// Starts the Avalonia headless app and blocks until initialisation completes.
    /// </summary>
    public static HeadlessSession Start(int timeoutSeconds = 30)
    {
        var session = new HeadlessSession();
        // Do NOT use 'using' here – the lambda captures 'ready' and may call Set()
        // concurrently with the timeout path which disposes it.
        var ready = new ManualResetEventSlim(false);

        BenchmarkApp.OnInitialized = () =>
        {
            session._lifetime =
                Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            ready.Set();
        };

        var thread = new Thread(() =>
        {
            AppBuilder.Configure<BenchmarkApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        });

        thread.IsBackground = true;
        thread.Name = "Avalonia-Headless-Benchmark";
        // STA is required on Windows for COM/COM-adjacent APIs used by Avalonia.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException($"Avalonia headless app did not start within {timeoutSeconds} s.");

        return session;
    }

    // ── Dispatcher helpers ────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="action"/> on the Avalonia UI thread and waits.</summary>
    public void Run(Action action) =>
        Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();

    /// <summary>
    /// Runs an async lambda on the Avalonia UI thread and blocks until the returned
    /// <see cref="System.Threading.Tasks.Task"/> completes.
    /// </summary>
    public void Run(Func<System.Threading.Tasks.Task> asyncAction)
    {
        // We cannot simply await DispatcherOperation<Task> because that operation
        // completes when the lambda *returns* (after the first await inside it), not
        // when the inner task finishes.  Use TCS as a signal instead.
        var tcs = new System.Threading.Tasks.TaskCompletionSource(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await asyncAction();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Flushes all pending <c>Normal</c>-priority dispatcher work by posting a
    /// <c>Background</c>-priority no-op and awaiting it.  Call this after triggering
    /// an operation that posts UI work items.
    /// </summary>
    public void FlushDispatcher() =>
        Dispatcher.UIThread.InvokeAsync(
            static () => { }, DispatcherPriority.Background)
            .GetAwaiter().GetResult();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime?.Shutdown(); }
        catch { /* ignore shutdown errors in benchmarks */ }
    }
}
