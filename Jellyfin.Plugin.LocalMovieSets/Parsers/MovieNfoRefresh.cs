using System;
using System.IO;

namespace Jellyfin.Plugin.LocalMovieSets.Parsers;

/// <summary>
/// Decides whether a movie's NFO is newer than the copy Jellyfin already stored.
/// The comparison is file time only. Parsing and applying the NFO stays with Jellyfin.
/// </summary>
public static class MovieNfoRefresh
{
    /// <summary>
    /// Returns true when <paramref name="nfoPath"/> exists and its last write time
    /// is strictly newer than the library item's last save.
    /// A library timestamp of <see cref="DateTime.MinValue"/> counts as older.
    /// </summary>
    /// <param name="nfoPath">Path to the movie NFO, or null when there is no file.</param>
    /// <param name="librarySavedUtc">When Jellyfin last saved the movie.</param>
    /// <returns>True when the movie should be refreshed from the NFO.</returns>
    public static bool ShouldRefresh(string? nfoPath, DateTime librarySavedUtc)
    {
        if (string.IsNullOrWhiteSpace(nfoPath) || !File.Exists(nfoPath))
        {
            return false;
        }

        DateTime nfoWriteUtc;
        try
        {
            nfoWriteUtc = File.GetLastWriteTimeUtc(nfoPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return IsNfoNewer(nfoWriteUtc, librarySavedUtc);
    }

    /// <summary>
    /// Returns true when the NFO write time is strictly newer than the library save time.
    /// </summary>
    /// <param name="nfoLastWriteUtc">Last write time of the NFO file.</param>
    /// <param name="librarySavedUtc">When Jellyfin last saved the movie.</param>
    /// <returns>True when the NFO is newer.</returns>
    public static bool IsNfoNewer(DateTime nfoLastWriteUtc, DateTime librarySavedUtc)
    {
        var nfo = AsUtc(nfoLastWriteUtc);
        if (nfo == DateTime.MinValue)
        {
            return false;
        }

        var saved = AsUtc(librarySavedUtc);
        if (saved == DateTime.MinValue)
        {
            return true;
        }

        return nfo > saved;
    }

    /// <summary>
    /// Picks the library timestamp to compare against: the later of the last
    /// metadata save and the last refresh. A refresh that re-reads an NFO without
    /// finding changes does not update <c>DateLastSaved</c>, so comparing with the
    /// save time alone would re-read a merely touched NFO on every sync.
    /// When neither was set, the item's modified time is used.
    /// </summary>
    /// <param name="dateLastSaved">Jellyfin <c>DateLastSaved</c>.</param>
    /// <param name="dateLastRefreshed">Jellyfin <c>DateLastRefreshed</c>.</param>
    /// <param name="dateModified">Jellyfin <c>DateModified</c>.</param>
    /// <returns>The timestamp that represents the stored movie.</returns>
    public static DateTime LibrarySavedUtc(DateTime dateLastSaved, DateTime dateLastRefreshed, DateTime dateModified)
    {
        var saved = AsUtc(dateLastSaved);
        var refreshed = AsUtc(dateLastRefreshed);
        var latest = saved > refreshed ? saved : refreshed;
        if (latest > DateTime.MinValue)
        {
            return latest;
        }

        return dateModified;
    }

    private static DateTime AsUtc(DateTime value)
    {
        if (value == DateTime.MinValue)
        {
            return DateTime.MinValue;
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
