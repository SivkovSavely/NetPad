using NetPad.ExecutionModel.ScriptServices;

namespace NetPad.Runtime.Tests.ExecutionModel;

// These tests exercise Util-level static state; join the presentation pipeline collection so
// they do not race other suites that reset that state.
[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class KeepRunningTests
{
    [Fact]
    public async Task WaitCompletesImmediately_WhenNoLeases()
    {
        KeepRunningManager.Reset();

        await KeepRunningManager.WaitUntilReleasedAsync(CancellationToken.None);

        Assert.False(KeepRunningManager.HasActiveLeases);
    }

    [Fact]
    public async Task WaitReleases_WhenAllLeasesDisposed()
    {
        KeepRunningManager.Reset();

        var lease = Util.KeepRunning();
        Assert.True(KeepRunningManager.HasActiveLeases);

        var wait = Task.Run(() => KeepRunningManager.WaitUntilReleasedAsync(CancellationToken.None));
        await Task.Delay(100); // Give the wait a chance to start while the lease is held.
        Assert.False(wait.IsCompleted);

        lease.Dispose();
        await wait;

        Assert.False(KeepRunningManager.HasActiveLeases);
    }

    [Fact]
    public async Task WaitWaits_UntilLeaseDisposed()
    {
        KeepRunningManager.Reset();
        var lease = Util.KeepRunning();

        var wait = KeepRunningManager.WaitUntilReleasedAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        lease.Dispose();
        await wait;

        Assert.False(KeepRunningManager.HasActiveLeases);
    }

    [Fact]
    public async Task CancellationReleasesWait()
    {
        KeepRunningManager.Reset();
        var lease = Util.KeepRunning();

        using var cts = new CancellationTokenSource();
        var wait = KeepRunningManager.WaitUntilReleasedAsync(cts.Token);
        Assert.False(wait.IsCompleted);

        cts.Cancel();
        await wait;

        lease.Dispose();
    }

    [Fact]
    public void ResetDisposesRegistrationsFromPreviousRun()
    {
        KeepRunningManager.Reset();
        var oldLease = Util.KeepRunning();
        Assert.True(KeepRunningManager.HasActiveLeases);

        // New run starting: previous leases are discarded and waiters released.
        KeepRunningManager.Reset();

        Assert.False(KeepRunningManager.HasActiveLeases);
        oldLease.Dispose(); // Disposing a stale lease must be safe.
        Assert.False(KeepRunningManager.HasActiveLeases);
    }
}

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class QueryCancelTokenTests
{
    [Fact]
    public void FreshTokenPerRunScope()
    {
        Util.BeginInteractiveRunScope();
        try
        {
            var firstToken = Util.QueryCancelToken;
            Assert.False(firstToken.IsCancellationRequested);

            Util.RequestCooperativeCancellation();
            Assert.True(Util.SoftCancellationRequested);
            Assert.True(Util.QueryCancelToken.IsCancellationRequested);
        }
        finally
        {
            Util.EndInteractiveRunScope();
        }

        // A new scope gets a fresh, uncancelled token.
        Util.BeginInteractiveRunScope();
        try
        {
            Assert.False(Util.SoftCancellationRequested);
            Assert.False(Util.QueryCancelToken.IsCancellationRequested);
        }
        finally
        {
            Util.EndInteractiveRunScope();
        }
    }
}
