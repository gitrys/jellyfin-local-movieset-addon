using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// Remaining findings of issue #10: library events during a sync, missing movie
/// files, mixed libraries in Mount Guard, theme copies, stacked files, duplicates.
/// </summary>
[Collection(PluginStateCollection.Name)]
public sealed class SyncRobustnessTests : IDisposable
{
    private readonly SyncTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<SyncStatusInfo> SyncAsync()
    {
        var status = await _h.Manager.ScanAndSyncAsync(CancellationToken.None, waitIfRunning: true);
        Assert.NotNull(status);
        return status!;
    }

    private static ItemChangeEventArgs MovieEvent() => new() { Item = new Movie { Id = Guid.NewGuid() } };

    // ── Library events ───────────────────────────────────────────────────

    [Fact]
    public async Task MovieAddedDuringSync_SchedulesAnotherSync()
    {
        _h.AddMovie("Alien", "Alien Collection");
        _h.AddMovie("Aliens", "Alien Collection");
        _h.DuringSync = () => _h.Manager.OnLibraryItemChanged(MovieEvent(), addedOrRemoved: true);

        await SyncAsync();

        Assert.True(_h.Manager.AutoSyncScheduled);
    }

    [Fact]
    public async Task MovieUpdatedDuringSync_DoesNotScheduleAnotherSync()
    {
        _h.AddMovie("Alien", "Alien Collection");
        _h.AddMovie("Aliens", "Alien Collection");
        _h.DuringSync = () => _h.Manager.OnLibraryItemChanged(MovieEvent(), addedOrRemoved: false);

        await SyncAsync();

        Assert.False(_h.Manager.AutoSyncScheduled);
    }

    [Fact]
    public void MovieEventOutsideSync_SchedulesSync_OtherItemsDoNot()
    {
        _h.Manager.OnLibraryItemChanged(new ItemChangeEventArgs { Item = new Series() }, addedOrRemoved: true);
        Assert.False(_h.Manager.AutoSyncScheduled);

        _h.Manager.OnLibraryItemChanged(MovieEvent(), addedOrRemoved: false);
        Assert.True(_h.Manager.AutoSyncScheduled);
    }

    [Fact]
    public async Task AutoSyncOff_MovieAddedDuringSync_SchedulesNothing()
    {
        _h.Config.EnableAutoSyncOnLibraryChange = false;
        _h.AddMovie("Alien", "Alien Collection");
        _h.AddMovie("Aliens", "Alien Collection");
        _h.DuringSync = () => _h.Manager.OnLibraryItemChanged(MovieEvent(), addedOrRemoved: true);

        await SyncAsync();

        Assert.False(_h.Manager.AutoSyncScheduled);
    }

    // ── Missing movie files ──────────────────────────────────────────────

    private Movie AddOfflineMovie(string name)
    {
        var movie = _h.AddMovie(name, "Alien Collection");
        Directory.Delete(Path.GetDirectoryName(movie.Path)!, true);
        return movie;
    }

    [Fact]
    public async Task MissingMovieFile_KeepsTheMovieInItsCollection()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = AddOfflineMovie("Aliens");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
        Assert.Equal(1, status.MissingMovieFiles);
        Assert.Equal(0, status.NfoParseErrors);
        Assert.Empty(_h.Removed);
        Assert.Contains(_h.Manager.GetStatusSnapshot().RecentLogs, l => l.Contains("1 movie file(s) were not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectionWhoseFilesAreAllMissing_IsNotDeletedAsOrphan()
    {
        _h.Config.DeleteOrphanedSets = true;
        _h.Config.MaxOrphanDeletionPercent = 0;
        var a = AddOfflineMovie("Alien");
        var b = AddOfflineMovie("Aliens");
        _h.AddBoxSet("Alien Collection", owned: true, a, b);

        await SyncAsync();

        Assert.Empty(_h.Deleted);
        Assert.Empty(_h.Removed);
    }

    [Fact]
    public async Task MovieWithFileButWithoutSet_IsStillRemoved()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var b = _h.AddMovie("Aliens", "Alien Collection");
        var c = _h.AddMovie("Heat", setName: null);
        _h.AddBoxSet("Alien Collection", owned: true, a, b, c);

        var status = await SyncAsync();

        Assert.Equal(0, status.MissingMovieFiles);
        Assert.Equal([c.Id], Assert.Single(_h.Removed).Movies);
    }

    // ── Mount Guard and mixed libraries ──────────────────────────────────

    [Theory]
    [InlineData(CollectionTypeOptions.mixed)]
    [InlineData(null)]
    public async Task OfflineMixedLibrary_AbortsSync(CollectionTypeOptions? type)
    {
        _h.AddBoxSet("Old Set", owned: true);
        _h.ExtraLibraries.Add(new VirtualFolderInfo
        {
            Name = "Mixed",
            CollectionType = type,
            Locations = [Path.Combine(_h.Root, "offline-mixed")]
        });

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.MountGuardAborted, status.LastRunOutcome);
        Assert.False(_h.HasWrites);
    }

    [Fact]
    public async Task OfflineMusicLibrary_DoesNotAbortSync()
    {
        _h.ExtraLibraries.Add(new VirtualFolderInfo
        {
            Name = "Music",
            CollectionType = CollectionTypeOptions.music,
            Locations = [Path.Combine(_h.Root, "offline-music")]
        });

        var status = await SyncAsync();

        Assert.Equal(SyncOutcomes.Success, status.LastRunOutcome);
    }

    // ── Theme copies ─────────────────────────────────────────────────────

    private (BoxSet BoxSet, string CollectionFolder) OwnedCollectionWithFolder()
    {
        var collectionFolder = Directory.CreateDirectory(Path.Combine(_h.Root, "collections", "Alien Collection [boxset]")).FullName;
        var boxSet = _h.AddBoxSet("Alien Collection", owned: true);
        boxSet.Path = collectionFolder;
        return (boxSet, collectionFolder);
    }

    private BoxSetMetadataProvider MetadataProvider() => new(
        _h.Library,
        _h.Manager,
        new SetNfoParser(NullLogger<SetNfoParser>.Instance),
        NullLogger<BoxSetMetadataProvider>.Instance);

    [Fact]
    public async Task NewThemeFormat_ReplacesOldThemeCopy()
    {
        _h.AddSetFolder("Alien Collection", "theme.flac");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        File.WriteAllText(Path.Combine(collectionFolder, "theme.mp3"), "old");

        await MetadataProvider().FetchAsync(boxSet, new MetadataRefreshOptions(Substitute.For<IDirectoryService>()), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "theme.flac")));
        Assert.False(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
    }

    private Task FetchAsync(BoxSet boxSet) =>
        MetadataProvider().FetchAsync(boxSet, new MetadataRefreshOptions(Substitute.For<IDirectoryService>()), CancellationToken.None);

    [Fact]
    public async Task ThemeRemovedFromSetFolder_CopyIsRemoved()
    {
        _h.AddSetFolder("Alien Collection");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        File.WriteAllText(Path.Combine(collectionFolder, "theme.mp3"), "old");
        File.WriteAllText(Path.Combine(collectionFolder, "poster.jpg"), "image");

        await FetchAsync(boxSet);

        Assert.False(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
        Assert.True(File.Exists(Path.Combine(collectionFolder, "poster.jpg")));
    }

    [Fact]
    public async Task SetFolderOffline_ThemeCopyStays()
    {
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        File.WriteAllText(Path.Combine(collectionFolder, "theme.mp3"), "copy");
        _h.Config.SetDataFolder = Path.Combine(_h.Root, "offline-share");

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
    }

    [Fact]
    public async Task NoSetFolderConfigured_ThemeCopyStays()
    {
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        File.WriteAllText(Path.Combine(collectionFolder, "theme.mp3"), "copy");
        _h.Config.SetDataFolder = string.Empty;

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
    }

    [Fact]
    public async Task ManualCollection_ThemeCopyStays()
    {
        _h.AddSetFolder("Christmas Movies");
        var collectionFolder = Directory.CreateDirectory(Path.Combine(_h.Root, "collections", "Christmas Movies [boxset]")).FullName;
        var boxSet = _h.AddBoxSet("Christmas Movies", owned: false);
        boxSet.Path = collectionFolder;
        File.WriteAllText(Path.Combine(collectionFolder, "theme.mp3"), "mine");
        Directory.CreateDirectory(Path.Combine(collectionFolder, "backdrops"));
        File.WriteAllText(Path.Combine(collectionFolder, "backdrops", "theme.mp4"), "mine");

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
        Assert.True(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.mp4")));
    }

    // ── Theme videos ─────────────────────────────────────────────────────

    [Fact]
    public async Task ThemeVideo_IsCopiedIntoBackdrops_NextToThemeSong()
    {
        _h.AddSetFolder("Alien Collection", "theme.mp3", "theme.mp4");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "theme.mp3")));
        Assert.True(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.mp4")));
        Assert.False(File.Exists(Path.Combine(collectionFolder, "theme.mp4")));
    }

    [Fact]
    public async Task PrefixedThemeVideo_IsFound()
    {
        _h.AddSetFolder("Alien Collection", "Alien Collection-theme.webm");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.webm")));
    }

    [Fact]
    public async Task ThemeVideoFormatChange_ReplacesOldCopy()
    {
        _h.AddSetFolder("Alien Collection", "theme.mp4");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        Directory.CreateDirectory(Path.Combine(collectionFolder, "backdrops"));
        File.WriteAllText(Path.Combine(collectionFolder, "backdrops", "theme.mkv"), "old");

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.mp4")));
        Assert.False(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.mkv")));
    }

    [Fact]
    public async Task ThemeVideoRemovedFromSetFolder_CopyAndEmptyFolderAreRemoved()
    {
        _h.AddSetFolder("Alien Collection");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        Directory.CreateDirectory(Path.Combine(collectionFolder, "backdrops"));
        File.WriteAllText(Path.Combine(collectionFolder, "backdrops", "theme.mp4"), "old");

        await FetchAsync(boxSet);

        Assert.False(Directory.Exists(Path.Combine(collectionFolder, "backdrops")));
    }

    [Fact]
    public async Task SetFolderOffline_ThemeVideoCopyStays()
    {
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        Directory.CreateDirectory(Path.Combine(collectionFolder, "backdrops"));
        File.WriteAllText(Path.Combine(collectionFolder, "backdrops", "theme.mp4"), "copy");
        _h.Config.SetDataFolder = Path.Combine(_h.Root, "offline-share");

        await FetchAsync(boxSet);

        Assert.True(File.Exists(Path.Combine(collectionFolder, "backdrops", "theme.mp4")));
    }

    [Fact]
    public async Task UnchangedThemeVideo_IsNotCopiedAgain()
    {
        _h.AddSetFolder("Alien Collection", "theme.mp4");
        var (boxSet, collectionFolder) = OwnedCollectionWithFolder();
        await FetchAsync(boxSet);

        // Same size and time as the source, different bytes: a new copy would restore the source bytes.
        var copy = Path.Combine(collectionFolder, "backdrops", "theme.mp4");
        var time = File.GetLastWriteTimeUtc(copy);
        File.WriteAllBytes(copy, [9, 9, 9]);
        File.SetLastWriteTimeUtc(copy, time);

        await FetchAsync(boxSet);

        Assert.Equal([9, 9, 9], File.ReadAllBytes(copy));
    }

    // ── Stacked files ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Alien-cd1", "Alien")]
    [InlineData("Alien.part2", "Alien")]
    [InlineData("Alien_disc1", "Alien")]
    [InlineData("Alien pt1", "Alien")]
    [InlineData("Alien - DVD2", "Alien")]
    [InlineData("Alien-cdb", "Alien")]
    [InlineData("Kill Bill Part 1", "Kill Bill")]
    [InlineData("Alien", null)]
    [InlineData("Alien 3", null)]
    [InlineData("Abcd1", null)]
    [InlineData("Ocean's Eleven Part", null)]
    public void StripStackingSuffix_RemovesOnlyTrailingPartMarker(string name, string? expected)
    {
        Assert.Equal(expected, MovieNfoParser.StripStackingSuffix(name));
    }

    [Fact]
    public async Task StackedFile_FindsNfoWithoutPartSuffix()
    {
        var a = _h.AddMovie("Alien", "Alien Collection");
        var stacked = _h.AddMovie("Aliens-cd1", "Alien Collection");
        var folder = Path.GetDirectoryName(stacked.Path)!;
        File.Move(Path.Combine(folder, "Aliens-cd1.nfo"), Path.Combine(folder, "Aliens.nfo"));

        await SyncAsync();

        var created = Assert.Single(_h.Created);
        Assert.Contains(stacked.Id.ToString(), created.ItemIdList);
        Assert.Contains(a.Id.ToString(), created.ItemIdList);
    }

    // ── Duplicate collection names ───────────────────────────────────────

    [Fact]
    public void Duplicates_OwnedWinsOverOlder()
    {
        var older = new BoxSet { Id = Guid.NewGuid(), Name = "Alien Collection", DateCreated = new DateTime(2020, 1, 1) };
        var owned = new BoxSet { Id = Guid.NewGuid(), Name = "Alien Collection", DateCreated = new DateTime(2024, 1, 1) };
        CollectionOwnership.MarkOwned(owned, "Alien Collection");

        Assert.Same(owned, LocalMovieSetManager.PickManagedDuplicate([older, owned]));
    }

    [Fact]
    public void Duplicates_WithoutMarker_OldestWins_RegardlessOfOrder()
    {
        var newer = new BoxSet { Id = Guid.NewGuid(), Name = "Alien Collection", DateCreated = new DateTime(2024, 1, 1) };
        var older = new BoxSet { Id = Guid.NewGuid(), Name = "Alien Collection", DateCreated = new DateTime(2020, 1, 1) };

        Assert.Same(older, LocalMovieSetManager.PickManagedDuplicate([newer, older]));
        Assert.Same(older, LocalMovieSetManager.PickManagedDuplicate([older, newer]));
    }

    [Fact]
    public void Duplicates_SameDate_LowestIdWins()
    {
        var date = new DateTime(2024, 1, 1);
        var first = new BoxSet { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "A", DateCreated = date };
        var second = new BoxSet { Id = new Guid("00000000-0000-0000-0000-000000000002"), Name = "A", DateCreated = date };

        Assert.Same(first, LocalMovieSetManager.PickManagedDuplicate([second, first]));
    }
}
