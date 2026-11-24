using System;
using System.Threading;
using Jellyfin.Plugin.LocalMovieSets.ScheduledTasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class SyncMovieSetsTaskTests
{
    private static SyncStatusInfo Run(string outcome, string? error = null)
        => new() { LastRunOutcome = outcome, LastErrorMessage = error };

    [Fact]
    public void Success_DoesNotThrow()
    {
        SyncMovieSetsTask.ThrowIfUnsuccessful(Run(SyncOutcomes.Success), CancellationToken.None);
    }

    [Fact]
    public void SuccessWithWarnings_DoesNotThrow()
    {
        var result = Run(SyncOutcomes.Success);
        result.OrphanDeletionBlocked = true;
        result.SetDataFolderUnavailable = true;

        SyncMovieSetsTask.ThrowIfUnsuccessful(result, CancellationToken.None);
    }

    [Fact]
    public void Failed_ThrowsWithErrorMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SyncMovieSetsTask.ThrowIfUnsuccessful(Run(SyncOutcomes.Failed, "Database locked"), CancellationToken.None));

        Assert.Contains("Database locked", ex.Message);
    }

    [Fact]
    public void MountGuardAborted_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SyncMovieSetsTask.ThrowIfUnsuccessful(Run(SyncOutcomes.MountGuardAborted), CancellationToken.None));

        Assert.Contains("Mount Guard", ex.Message);
    }

    [Fact]
    public void Cancelled_ThrowsOperationCanceled()
    {
        Assert.Throws<OperationCanceledException>(
            () => SyncMovieSetsTask.ThrowIfUnsuccessful(Run(SyncOutcomes.Cancelled), CancellationToken.None));
    }

    [Fact]
    public void NotStarted_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => SyncMovieSetsTask.ThrowIfUnsuccessful(null, cts.Token));
    }

    [Fact]
    public void NotStarted_WithoutCancellation_DoesNotThrow()
    {
        SyncMovieSetsTask.ThrowIfUnsuccessful(null, CancellationToken.None);
    }
}
