using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalMovieSets;

/// <summary>
/// Naming convention used by tinyMediaManager for dedicated set NFO files.
/// </summary>
public enum NfoNamingConvention
{
    /// <summary>
    /// &lt;SetDataFolder&gt;/&lt;SetName&gt;/&lt;SetName&gt;.nfo
    /// This is the most common TMM convention.
    /// </summary>
    SetSubfolder = 0,

    /// <summary>
    /// &lt;SetDataFolder&gt;/&lt;SetName&gt;.nfo
    /// Flat file in the root set data folder.
    /// </summary>
    FlatFile = 1,

    /// <summary>
    /// &lt;SetDataFolder&gt;/&lt;SetName&gt;/collection.nfo
    /// Kodi "collection.nfo" style inside a subfolder.
    /// </summary>
    CollectionNfo = 2
}

/// <summary>
/// Mode for calculating the collection's release date.
/// </summary>
public enum CollectionReleaseDateMode
{
    /// <summary>
    /// Do not calculate or set release dates for collections.
    /// </summary>
    DoNotCalculate = 0,

    /// <summary>
    /// Always calculate the release date based on the oldest movie in the collection.
    /// </summary>
    AlwaysOverwrite = 1,

    /// <summary>
    /// Only calculate and set the release date if the collection has no release date currently set.
    /// </summary>
    SetOnlyIfEmpty = 2
}

/// <summary>
/// Plugin configuration stored as XML in the Jellyfin data directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the path to tinyMediaManager's "Movie Set Data Folder".
    /// Leave empty to skip dedicated set NFO and artwork lookup.
    /// </summary>
    public string SetDataFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the NFO naming convention used by tinyMediaManager for set files.
    /// </summary>
    public NfoNamingConvention NfoNaming { get; set; } = NfoNamingConvention.SetSubfolder;

    /// <summary>
    /// Gets or sets the mode for calculating the collection's release date.
    /// </summary>
    public CollectionReleaseDateMode CollectionReleaseDate { get; set; } = CollectionReleaseDateMode.DoNotCalculate;

    /// <summary>
    /// Gets or sets the minimum number of movies that must be present in the library
    /// for a set to be created as a Jellyfin collection. Defaults to 1.
    /// </summary>
    public int MinimumMovies { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to delete Jellyfin collections
    /// that no longer have any movies with a matching &lt;set&gt; tag in their NFO.
    /// </summary>
    public bool DeleteOrphanedSets { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether orphan deletion should also
    /// remove collections that have a TMDB or IMDb provider ID.
    /// When false (default), collections with a provider ID are kept as a safety guard.
    /// </summary>
    public bool DeleteSetsWithProviderId { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to overwrite existing collection
    /// artwork with images found in the TMM set folder.
    /// </summary>
    public bool UpdateExistingArtwork { get; set; } = false;

    /// <summary>
    /// Gets or sets the default sort field for newly created collections.
    /// </summary>
    public string CollectionSortBy { get; set; } = "Name";

    /// <summary>
    /// Gets or sets the default sort direction for newly created collections.
    /// </summary>
    public string CollectionSortOrder { get; set; } = "Ascending";

    /// <summary>
    /// Gets or sets a value indicating whether to enable the Mount Guard.
    /// When true, checks if all configured movie library folders are accessible and not empty before syncing.
    /// </summary>
    public bool EnableMountGuard { get; set; } = true;
}


