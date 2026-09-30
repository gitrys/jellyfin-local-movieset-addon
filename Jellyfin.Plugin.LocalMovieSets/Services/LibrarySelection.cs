namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Interprets the movie-library checklist.
/// An empty id list scans every library only while the legacy include-all flag is still true.
/// </summary>
internal static class LibrarySelection
{
    /// <summary>
    /// Returns whether the user saved an empty library selection and sync must not scan.
    /// </summary>
    /// <param name="includedLibraryIds">Saved library ids.</param>
    /// <param name="includeAllLibraries">Legacy flag: empty ids still mean every library.</param>
    /// <returns><c>true</c> when no library should be scanned.</returns>
    public static bool IsNoneSelected(string[]? includedLibraryIds, bool includeAllLibraries)
    {
        return (includedLibraryIds is null || includedLibraryIds.Length == 0) && !includeAllLibraries;
    }
}
