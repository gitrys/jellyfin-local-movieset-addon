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
    /// Gets or sets a value indicating whether to search for set artwork inside individual movie folders
    /// as a fallback when not found in the dedicated set data folder.
    /// </summary>
    public bool EnableMovieFolderArtworkFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the display order for collections.
    /// Supported values: "Default" (creation/link order), "PremiereDate", "SortName".
    /// Legacy values from older versions are treated as "Default".
    /// </summary>
    public string CollectionSortBy { get; set; } = "Default";

    /// <summary>
    /// Gets or sets the default sort direction for newly created collections.
    /// </summary>
    public string CollectionSortOrder { get; set; } = "Ascending";

    /// <summary>
    /// Gets or sets a value indicating whether to enable the Mount Guard.
    /// When true, checks if all configured movie library folders are accessible and not empty before syncing.
    /// </summary>
    public bool EnableMountGuard { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to aggregate community ratings from member movies.
    /// </summary>
    public bool AggregateRatings { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to aggregate tags from member movies.
    /// </summary>
    public bool AggregateTags { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to aggregate cast and crew from member movies.
    /// </summary>
    public bool AggregatePeople { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to aggregate genres from member movies.
    /// </summary>
    public bool AggregateGenres { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to aggregate production studios from member movies.
    /// </summary>
    public bool AggregateStudios { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of tags to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxTags { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum number of actors to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxActors { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of directors to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxDirectors { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of writers to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxWriters { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of genres to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxGenres { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of studios to apply to a collection (0 = all / unlimited).
    /// Ranked by frequency across member movies.
    /// </summary>
    public int MaxStudios { get; set; } = 0;

    /// <summary>
    /// Gets or sets the list of library IDs to include in the sync.
    /// If empty, all movie libraries are scanned.
    /// </summary>
    public string[] IncludedLibraryIds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to automatically trigger a sync
    /// after movies are added or updated in the library (debounced by 30 seconds).
    /// </summary>
    public bool EnableAutoSyncOnLibraryChange { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the one-time cleanup of sort titles
    /// written by old plugin versions has already been performed.
    /// </summary>
    public bool LegacySortTitleCleanupCompleted { get; set; } = false;
}


