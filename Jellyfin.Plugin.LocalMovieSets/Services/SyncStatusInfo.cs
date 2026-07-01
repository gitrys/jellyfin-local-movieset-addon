using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Well-known values for <see cref="SyncStatusInfo.LastRunOutcome"/>.
/// Kept as strings so the API payload is unambiguous for the config page.
/// </summary>
public static class SyncOutcomes
{
    /// <summary>No sync has run since the server started.</summary>
    public const string NeverRan = "NeverRan";

    /// <summary>A sync is currently running.</summary>
    public const string Running = "Running";

    /// <summary>The last sync completed successfully.</summary>
    public const string Success = "Success";

    /// <summary>The last sync failed with an error.</summary>
    public const string Failed = "Failed";

    /// <summary>The last sync was cancelled.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>The last sync was aborted by the Mount Guard.</summary>
    public const string MountGuardAborted = "MountGuardAborted";
}

/// <summary>
/// Statistics and outcome of the most recent sync run.
/// Held in memory only — resets when the server restarts.
/// </summary>
public class SyncStatusInfo
{
    /// <summary>Gets or sets a value indicating whether a sync is currently running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>Gets or sets when the last run started (UTC).</summary>
    public DateTime? LastRunStartedUtc { get; set; }

    /// <summary>Gets or sets when the last run completed (UTC).</summary>
    public DateTime? LastRunCompletedUtc { get; set; }

    /// <summary>Gets or sets the outcome of the last run. See <see cref="SyncOutcomes"/>.</summary>
    public string LastRunOutcome { get; set; } = SyncOutcomes.NeverRan;

    /// <summary>Gets or sets the number of movies queried from the library.</summary>
    public int MoviesScanned { get; set; }

    /// <summary>Gets or sets the number of movies that belong to a set.</summary>
    public int MoviesInSets { get; set; }

    /// <summary>Gets or sets the number of distinct sets found in NFOs.</summary>
    public int SetsFound { get; set; }

    /// <summary>Gets or sets the number of collections created.</summary>
    public int CollectionsCreated { get; set; }

    /// <summary>Gets or sets the number of collections whose membership or settings changed.</summary>
    public int CollectionsUpdated { get; set; }

    /// <summary>Gets or sets the number of orphaned collections deleted.</summary>
    public int CollectionsDeleted { get; set; }

    /// <summary>Gets or sets the number of NFO files that could not be parsed.</summary>
    public int NfoParseErrors { get; set; }

    /// <summary>Gets or sets the collection names that occur more than once in Jellyfin.</summary>
    public IReadOnlyList<string> DuplicateCollectionNames { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the error message of the last failed run, if any.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Creates a shallow copy of this snapshot.</summary>
    /// <returns>A copy safe to hand out to API consumers.</returns>
    public SyncStatusInfo Clone() => (SyncStatusInfo)MemberwiseClone();
}

/// <summary>
/// A set that would be created or skipped by a sync, as reported by the preview.
/// </summary>
public class PreviewSetInfo
{
    /// <summary>Gets or sets the set name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of library movies in the set.</summary>
    public int MovieCount { get; set; }
}

/// <summary>
/// An existing collection whose membership would change, as reported by the preview.
/// </summary>
public class PreviewUpdateInfo
{
    /// <summary>Gets or sets the collection name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of movies that would be added.</summary>
    public int MoviesToAdd { get; set; }

    /// <summary>Gets or sets the number of movies that would be removed.</summary>
    public int MoviesToRemove { get; set; }
}

/// <summary>
/// An orphaned collection that would be kept, with the reason it is protected.
/// </summary>
public class PreviewProtectedInfo
{
    /// <summary>Gets or sets the collection name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets why the collection would not be deleted.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of a read-only dry run showing what the next sync would do
/// under the currently saved plugin settings.
/// </summary>
public class SyncPreviewResult
{
    /// <summary>Gets or sets a value indicating whether a sync was running (preview not computed).</summary>
    public bool SyncRunning { get; set; }

    /// <summary>Gets or sets a value indicating whether the Mount Guard blocked the preview.</summary>
    public bool MountGuardBlocked { get; set; }

    /// <summary>Gets or sets an error message when the preview could not be computed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the collections that would be created.</summary>
    public List<PreviewSetInfo> ToCreate { get; set; } = [];

    /// <summary>Gets or sets the sets skipped because they are below the movie minimum.</summary>
    public List<PreviewSetInfo> BelowMinimum { get; set; } = [];

    /// <summary>Gets or sets the existing collections whose membership would change.</summary>
    public List<PreviewUpdateInfo> ToUpdate { get; set; } = [];

    /// <summary>Gets or sets the number of existing collections with no membership changes.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Gets or sets the orphaned collections that would be deleted.</summary>
    public List<string> ToDelete { get; set; } = [];

    /// <summary>Gets or sets the orphaned collections that would be kept, with reasons.</summary>
    public List<PreviewProtectedInfo> ProtectedOrphans { get; set; } = [];
}
