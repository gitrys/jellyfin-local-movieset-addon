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
    /// Gets or sets a value indicating whether to overwrite existing collection
    /// artwork with images found in the TMM set folder.
    /// </summary>
    public bool UpdateExistingArtwork { get; set; } = false;

    /// <summary>
    /// Gets or sets the default display order for newly created collections.
    /// Valid values: "SortName", "PremiereDate", "DateModified".
    /// </summary>
    public string CollectionDisplayOrder { get; set; } = "SortName";
}


