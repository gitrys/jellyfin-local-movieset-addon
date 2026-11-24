using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// The image and metadata providers only act on collections the plugin manages.
/// </summary>
[Collection(PluginStateCollection.Name)]
public sealed class CollectionProviderTests : IDisposable
{
    private readonly SyncTestHarness _h = new();
    private readonly BoxSetImageProvider _images;
    private readonly BoxSetMetadataProvider _metadata;

    public CollectionProviderTests()
    {
        _images = new BoxSetImageProvider(_h.Manager, NullLogger<BoxSetImageProvider>.Instance);
        _metadata = new BoxSetMetadataProvider(
            _h.Library,
            _h.Manager,
            new SetNfoParser(NullLogger<SetNfoParser>.Instance),
            NullLogger<BoxSetMetadataProvider>.Instance);
    }

    public void Dispose() => _h.Dispose();

    private static MetadataRefreshOptions RefreshOptions() => new(Substitute.For<IDirectoryService>());

    // ── Image provider ───────────────────────────────────────────────────

    [Fact]
    public async Task OwnedCollection_GetsPosterFromSetFolder_AsStream()
    {
        _h.AddSetFolder("Alien Collection", "poster.jpg");
        var boxSet = _h.AddBoxSet("Alien Collection", owned: true);

        Assert.Contains(ImageType.Primary, _images.GetSupportedImages(boxSet));
        var response = await _images.GetImage(boxSet, ImageType.Primary, CancellationToken.None);

        Assert.True(response.HasImage);
        Assert.NotNull(response.Stream);
        Assert.Null(response.Path);
        await response.Stream!.DisposeAsync();
    }

    [Fact]
    public async Task ManualCollection_GetsNoArtwork_EvenWithMatchingSetFolder()
    {
        _h.AddSetFolder("Christmas Movies", "poster.jpg");
        var boxSet = _h.AddBoxSet("Christmas Movies", owned: false);

        Assert.Empty(_images.GetSupportedImages(boxSet));
        var response = await _images.GetImage(boxSet, ImageType.Primary, CancellationToken.None);

        Assert.False(response.HasImage);
    }

    [Fact]
    public async Task UnreachableSetFolder_GivesNoArtwork()
    {
        _h.AddSetFolder("Alien Collection", "poster.jpg");
        var boxSet = _h.AddBoxSet("Alien Collection", owned: true);
        _h.Config.SetDataFolder = Path.Combine(_h.Root, "offline-share");

        var response = await _images.GetImage(boxSet, ImageType.Primary, CancellationToken.None);

        Assert.False(response.HasImage);
    }

    // ── Metadata provider ────────────────────────────────────────────────

    [Fact]
    public async Task ManualCollection_IsNotTouchedByMetadataProvider()
    {
        // A set NFO with the same name exists and would change the overview.
        var folder = _h.AddSetFolder("Christmas Movies");
        File.WriteAllText(
            Path.Combine(folder, "Christmas Movies.nfo"),
            "<set><title>Christmas Movies</title><overview>From the NFO.</overview></set>");
        var boxSet = _h.AddBoxSet("Christmas Movies", owned: false);
        boxSet.Overview = "My own text";

        var result = await _metadata.FetchAsync(boxSet, RefreshOptions(), CancellationToken.None);

        Assert.Equal(ItemUpdateType.None, result);
        Assert.Equal("My own text", boxSet.Overview);
        Assert.False(Services.CollectionOwnership.IsOwned(boxSet));
    }

    [Fact]
    public async Task OwnedCollection_GetsSetNfoOverview()
    {
        var folder = _h.AddSetFolder("Alien Collection");
        File.WriteAllText(
            Path.Combine(folder, "Alien Collection.nfo"),
            "<set><title>Alien Collection</title><overview>Four films.</overview></set>");
        var boxSet = _h.AddBoxSet("Alien Collection", owned: true);

        var result = await _metadata.FetchAsync(boxSet, RefreshOptions(), CancellationToken.None);

        Assert.Equal(ItemUpdateType.MetadataEdit, result);
        Assert.Equal("Four films.", boxSet.Overview);
    }
}
