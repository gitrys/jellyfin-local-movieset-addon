using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Marks collections this plugin manages with a dedicated provider ID.
/// Delete operations and the image provider only act on marked collections,
/// so manual collections and collections from other sources are left alone.
/// </summary>
public static class CollectionOwnership
{
    /// <summary>
    /// Provider ID key of the ownership marker. The value is the set name.
    /// </summary>
    public const string ProviderKey = "LocalMovieSets";

    /// <summary>
    /// Returns whether the item carries this plugin's ownership marker.
    /// </summary>
    /// <param name="item">The collection.</param>
    /// <returns><c>true</c> when the marker is present.</returns>
    public static bool IsOwned(IHasProviderIds item)
    {
        return !string.IsNullOrWhiteSpace(item.GetProviderId(ProviderKey));
    }

    /// <summary>
    /// Sets the ownership marker to the given set name.
    /// </summary>
    /// <param name="item">The collection.</param>
    /// <param name="setName">Set name stored as the marker value.</param>
    /// <returns><c>true</c> when the marker was added or changed.</returns>
    public static bool MarkOwned(IHasProviderIds item, string setName)
    {
        if (string.Equals(item.GetProviderId(ProviderKey), setName, StringComparison.Ordinal))
        {
            return false;
        }

        item.SetProviderId(ProviderKey, setName);
        return true;
    }

    /// <summary>
    /// Returns whether the plugin should apply metadata and artwork to the collection:
    /// it carries the marker, or its name is a set found by the most recent sync.
    /// </summary>
    /// <param name="item">The collection.</param>
    /// <param name="isManagedSet">Checks a name against the sets of the most recent sync.</param>
    /// <returns><c>true</c> when the collection is managed by this plugin.</returns>
    public static bool IsManaged(BaseItem item, Func<string, bool> isManagedSet)
    {
        if (IsOwned(item))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(item.Name) && isManagedSet(item.Name);
    }
}
