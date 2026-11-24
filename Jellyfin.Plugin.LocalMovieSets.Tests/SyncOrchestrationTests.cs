using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Providers;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// End-to-end runs of <see cref="LocalMovieSetManager.ScanAndSyncAsync"/> against
/// substituted Jellyfin services and real movie NFO files.
/// </summary>
[Collection(PluginStateCollection.Name)]
public sealed class SyncOrchestrationTests : IDisposable
{
    private readonly SyncTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<SyncStatusInfo> SyncAsync()
    {
        var status = await _h.Manager.ScanAndSyncAsync(CancellationToken.None, waitIfRunning: true);
        Assert.NotNull(status);
        return status!;
    }

    [Fact]
    public async Task NewSet_CreatesCollection_WithMembersInReleaseOrder()
    {
        var aliens = _h.AddMovie("Aliens", "Alien Collection", year: 1986);
        var alien = _h.AddMovie("Alien", "Alien Collection", year: 1979);
        _h.AddMovie("Heat", setName: null);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(1, status.SetsFound);
        Assert.Equal(1, status.CollectionsCreated);
        var created = Assert.Single(_h.Created);
        Assert.Equal("Alien Collection", created.Name);
        Assert.Equal([alien.Id.ToString(), aliens.Id.ToString()], created.ItemIdList);
        _h.Providers.Received(1).QueueRefresh(
            _h.BoxSets.Single().Id, Arg.Any<MetadataRefreshOptions>(), Arg.Any<RefreshPriority>());
    }

    [Fact]
    public async Task SetBelowMinimum_IsNotCreated()
    {
        _h.AddMovie("Alien", "Alien Collection");

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Empty(_h.Created);
    }

    [Fact]
    public async Task ExistingCollection_AddsNewMembers_AndRemovesMoviesLeavingTheSet()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        var c = _h.AddMovie("Heat", setName: null);
        var boxSet = _h.AddBoxSet("Alien Collection", owned: true, a, c);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(1, status.CollectionsUpdated);
        var added = Assert.Single(_h.Added);
        Assert.Equal(boxSet.Id, added.Collection);
        Assert.Equal([b.Id], added.Movies);
        var removed = Assert.Single(_h.Removed);
        Assert.Equal(boxSet.Id, removed.Collection);
        Assert.Equal([c.Id], removed.Movies);
        Assert.Equal(
            new[] { a.Id, b.Id }.OrderBy(id => id),
            _h.MembersOf("Alien Collection").Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task UpToDateCollection_IsNotChanged()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(0, status.CollectionsUpdated);
        Assert.False(_h.HasWrites);
    }

    [Fact]
    public async Task UnreadableNfo_KeepsTheMovieInItsCollection()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection", brokenNfo: true);
        _h.AddBoxSet("Alien Collection", owned: true, a, b);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(1, status.NfoParseErrors);
        Assert.Empty(_h.Removed);
        Assert.Contains(_h.Manager.GetStatusSnapshot().RecentLogs, l => l.Contains("Kept 1 movie(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectionWithOnlyUnreadableMembers_IsNotDeletedAsOrphan()
    {
        _h.Config.DeleteOrphanedSets = true;
        _h.Config.MaxOrphanDeletionPercent = 0;
        var a = _h.AddMovie("Alien", "Alien Collection", brokenNfo: true);
        _h.AddBoxSet("Alien Collection", owned: true, a);

        await SyncAsync();

        Assert.Empty(_h.Deleted);
        Assert.Empty(_h.Removed);
    }

    [Fact]
    public async Task UnreachableSetFolder_StillSyncsMembers_AndWarns()
    {
        _h.Config.SetDataFolder = Path.Combine(_h.Root, "offline-share");
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        _h.AddBoxSet("Alien Collection", owned: true, a);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.True(status.SetDataFolderUnavailable);
        Assert.Equal([b.Id], Assert.Single(_h.Added).Movies);
    }
}
