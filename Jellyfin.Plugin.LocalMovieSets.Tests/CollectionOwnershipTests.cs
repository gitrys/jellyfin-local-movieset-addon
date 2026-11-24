using Jellyfin.Plugin.LocalMovieSets.Providers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class CollectionOwnershipTests
{
    [Fact]
    public void IsOwned_NewCollection_ReturnsFalse()
    {
        var boxSet = new BoxSet { Name = "Alien Collection" };

        Assert.False(CollectionOwnership.IsOwned(boxSet));
    }

    [Fact]
    public void IsOwned_TmdbIdAlone_ReturnsFalse()
    {
        // A TMDB ID (from a set NFO or from Jellyfin's own TMDB collections)
        // must not count as ownership.
        var boxSet = new BoxSet { Name = "Alien Collection" };
        boxSet.SetProviderId(MetadataProvider.Tmdb, "8091");

        Assert.False(CollectionOwnership.IsOwned(boxSet));
    }

    [Fact]
    public void MarkOwned_SetsMarkerWithSetName()
    {
        var boxSet = new BoxSet { Name = "Alien Collection" };

        var changed = CollectionOwnership.MarkOwned(boxSet, "Alien Collection");

        Assert.True(changed);
        Assert.True(CollectionOwnership.IsOwned(boxSet));
        Assert.Equal("Alien Collection", boxSet.GetProviderId(CollectionOwnership.ProviderKey));
    }

    [Fact]
    public void MarkOwned_SameValue_ReturnsFalse()
    {
        var boxSet = new BoxSet { Name = "Alien Collection" };
        CollectionOwnership.MarkOwned(boxSet, "Alien Collection");

        Assert.False(CollectionOwnership.MarkOwned(boxSet, "Alien Collection"));
    }

    [Fact]
    public void MarkOwned_DifferentValue_UpdatesMarker()
    {
        var boxSet = new BoxSet { Name = "Alien Collection" };
        CollectionOwnership.MarkOwned(boxSet, "alien collection");

        Assert.True(CollectionOwnership.MarkOwned(boxSet, "Alien Collection"));
        Assert.Equal("Alien Collection", boxSet.GetProviderId(CollectionOwnership.ProviderKey));
    }

    [Fact]
    public void IsManaged_OwnedCollection_ReturnsTrueWithoutCurrentSet()
    {
        var boxSet = new BoxSet { Name = "Old Set" };
        CollectionOwnership.MarkOwned(boxSet, "Old Set");

        Assert.True(CollectionOwnership.IsManaged(boxSet, _ => false));
    }

    [Fact]
    public void IsManaged_NameIsCurrentSet_ReturnsTrue()
    {
        var boxSet = new BoxSet { Name = "Alien Collection" };

        Assert.True(CollectionOwnership.IsManaged(boxSet, name => name == "Alien Collection"));
    }

    [Fact]
    public void IsManaged_ForeignCollection_ReturnsFalse()
    {
        var boxSet = new BoxSet { Name = "Christmas Movies" };

        Assert.False(CollectionOwnership.IsManaged(boxSet, name => name == "Alien Collection"));
    }

    [Fact]
    public void ExternalId_SupportsOnlyCollections()
    {
        var externalId = new LocalMovieSetsExternalId();

        Assert.True(externalId.Supports(new BoxSet()));
        Assert.False(externalId.Supports(new Movie()));
        Assert.Equal(CollectionOwnership.ProviderKey, externalId.Key);
        Assert.Equal(ExternalIdMediaType.BoxSet, externalId.Type);
    }
}
