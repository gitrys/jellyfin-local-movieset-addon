using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.LocalMovieSets.Providers;

/// <summary>
/// Registers the ownership marker as an external ID for collections, so it is
/// shown in Jellyfin's metadata editor and kept when the collection is saved there.
/// </summary>
public class LocalMovieSetsExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Local Movie Sets";

    /// <inheritdoc />
    public string Key => CollectionOwnership.ProviderKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.BoxSet;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is BoxSet;
}
