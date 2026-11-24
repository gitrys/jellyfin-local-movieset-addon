using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// Mount Guard, orphan deletion and Force Rebuild: the code paths that delete collections.
/// </summary>
[Collection(PluginStateCollection.Name)]
public sealed class DeletePathTests : IDisposable
{
    private readonly SyncTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<SyncStatusInfo> SyncAsync()
    {
        var status = await _h.Manager.ScanAndSyncAsync(CancellationToken.None, waitIfRunning: true);
        Assert.NotNull(status);
        return status!;
    }

    private void TakeLibraryOffline() => _h.LibraryLocations.Add(Path.Combine(_h.Root, "offline-library"));

    // ── Mount Guard ──────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_MountGuardAbort_WritesNothing()
    {
        _h.Config.DeleteOrphanedSets = true;
        _h.Config.MaxOrphanDeletionPercent = 0;
        var a = _h.AddMovie("Alien", "Alien Collection");
        _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a);
        _h.AddBoxSet("Old Set", owned: true);
        TakeLibraryOffline();

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.MountGuardAborted, status.LastRunOutcome);
        Assert.False(_h.HasWrites);
    }

    [Fact]
    public async Task Sync_EmptyLibraryFolder_IsTreatedAsOffline()
    {
        _h.AddBoxSet("Old Set", owned: true);
        _h.LibraryLocations.Add(Directory.CreateDirectory(Path.Combine(_h.Root, "empty-mount")).FullName);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.MountGuardAborted, status.LastRunOutcome);
        Assert.False(_h.HasWrites);
    }

    [Fact]
    public async Task ForceRebuild_MountGuardAbort_DeletesNothing()
    {
        _h.AddBoxSet("Alien Collection", owned: true);
        _h.AddBoxSet("Christmas Movies", owned: false);
        TakeLibraryOffline();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Manager.ForceRebuildAsync(deleteAll: true, CancellationToken.None));

        Assert.False(_h.HasWrites);
        Assert.Equal(SyncOutcomes.MountGuardAborted, _h.Manager.GetStatusSnapshot().LastRunOutcome);
        Assert.Contains(_h.Activity.CreatedEntries, e => e.Name == "Local Movie Sets Force Rebuild (all collections) Aborted");
    }

    // ── Orphan deletion ──────────────────────────────────────────────────

    [Fact]
    public async Task OrphanDeletion_Off_DeletesNothing()
    {
        _h.AddBoxSet("Old Set", owned: true);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Empty(_h.Deleted);
    }

    [Fact]
    public async Task OrphanDeletion_OnlyDeletesPluginOwnedCollections()
    {
        _h.Config.DeleteOrphanedSets = true;
        _h.Config.MaxOrphanDeletionPercent = 0;
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);
        _h.AddBoxSet("Old Set", owned: true);
        _h.AddBoxSet("Christmas Movies", owned: false, a);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(1, status.CollectionsDeleted);
        Assert.Equal(["Old Set"], _h.Deleted.Select(d => d.Name));
    }

    [Fact]
    public async Task OrphanDeletion_AboveLimit_DeletesNothing()
    {
        _h.Config.DeleteOrphanedSets = true;
        _h.Config.MaxOrphanDeletionPercent = 25;
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);
        for (var i = 1; i <= 5; i++)
        {
            _h.AddBoxSet("Old Set " + i, owned: true);
        }

        var status = await SyncAsync();

        Assert.True(status.OrphanDeletionBlocked);
        Assert.Empty(_h.Deleted);
        Assert.Contains(_h.Manager.GetStatusSnapshot().RecentLogs, l => l.Contains("Orphan cleanup skipped", StringComparison.Ordinal));
    }

    // ── Force Rebuild ────────────────────────────────────────────────────

    [Fact]
    public async Task ForceRebuild_PluginOnly_KeepsManualCollections_AndRecreates()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);
        _h.AddBoxSet("Christmas Movies", owned: false, a);

        await _h.Manager.ForceRebuildAsync(deleteAll: false, CancellationToken.None);

        Assert.Equal(["Alien Collection"], _h.Deleted.Select(d => d.Name));
        Assert.Equal(["Alien Collection"], _h.Created.Select(c => c.Name));
        Assert.Contains(_h.BoxSets, x => x.Name == "Christmas Movies");
        var status = _h.Manager.GetStatusSnapshot();
        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(SyncRunKinds.ForceRebuild, status.RunKind);
        Assert.Equal(1, status.RebuildDeleted);
    }

    [Fact]
    public async Task ForceRebuild_All_DeletesEveryCollection()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);
        _h.AddBoxSet("Christmas Movies", owned: false, a);

        await _h.Manager.ForceRebuildAsync(deleteAll: true, CancellationToken.None);

        Assert.Equal(
            new[] { "Alien Collection", "Christmas Movies" }.Order(),
            _h.Deleted.Select(d => d.Name).Order());
        Assert.Equal(["Alien Collection"], _h.Created.Select(c => c.Name));
        Assert.Equal(SyncRunKinds.ForceRebuildAll, _h.Manager.GetStatusSnapshot().RunKind);
    }

    [Fact]
    public async Task ForceRebuild_DeleteFailure_IsCounted_AndSyncStillRuns()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a);
        _h.FailDeleteFor.Add("Alien Collection");

        await _h.Manager.ForceRebuildAsync(deleteAll: false, CancellationToken.None);

        var status = _h.Manager.GetStatusSnapshot();
        Assert.Equal(1, status.RebuildDeleteFailures);
        Assert.Equal(0, status.RebuildDeleted);
        Assert.Empty(_h.Created);
        Assert.Equal([b.Id], Assert.Single(_h.Added).Movies);
    }
}
