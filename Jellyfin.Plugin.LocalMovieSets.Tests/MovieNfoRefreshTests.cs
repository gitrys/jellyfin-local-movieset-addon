using System;
using System.IO;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class MovieNfoRefreshTests : IDisposable
{
    private readonly string _tempDirectory;

    public MovieNfoRefreshTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalMovieSets_NfoRefresh_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void ShouldRefresh_MissingFile_ReturnsFalse()
    {
        var missing = Path.Combine(_tempDirectory, "movie.nfo");

        var result = MovieNfoRefresh.ShouldRefresh(missing, DateTime.UtcNow);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRefresh_NfoNewerThanLibrarySave_ReturnsTrue()
    {
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        File.WriteAllText(nfoPath, "<movie><title>Sample</title></movie>");
        var librarySaved = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(nfoPath, librarySaved.AddMinutes(5));

        var result = MovieNfoRefresh.ShouldRefresh(nfoPath, librarySaved);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRefresh_NfoOlderThanLibrarySave_ReturnsFalse()
    {
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        File.WriteAllText(nfoPath, "<movie><title>Sample</title></movie>");
        var nfoWritten = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(nfoPath, nfoWritten);

        var result = MovieNfoRefresh.ShouldRefresh(nfoPath, nfoWritten.AddMinutes(5));

        Assert.False(result);
    }

    [Fact]
    public void IsNfoNewer_LibraryNeverSaved_ReturnsTrue()
    {
        var nfoWritten = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.IsNfoNewer(nfoWritten, DateTime.MinValue);

        Assert.True(result);
    }

    [Fact]
    public void LibrarySavedUtc_PrefersDateLastSaved_OverModified()
    {
        var saved = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.LibrarySavedUtc(saved, DateTime.MinValue, modified);

        Assert.Equal(saved, result);
    }

    [Fact]
    public void LibrarySavedUtc_UsesLaterRefresh()
    {
        var saved = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var refreshed = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.LibrarySavedUtc(saved, refreshed, DateTime.MinValue);

        Assert.Equal(refreshed, result);
    }

    [Fact]
    public void LibrarySavedUtc_UsesLaterSave()
    {
        var saved = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var refreshed = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.LibrarySavedUtc(saved, refreshed, DateTime.MinValue);

        Assert.Equal(saved, result);
    }

    [Fact]
    public void LibrarySavedUtc_NeitherSet_UsesModified()
    {
        var modified = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.LibrarySavedUtc(DateTime.MinValue, DateTime.MinValue, modified);

        Assert.Equal(modified, result);
    }

    [Fact]
    public void ShouldRefresh_TouchedNfoAlreadyReRead_ReturnsFalse()
    {
        // Seen on a server: the NFO was written 20 s after Jellyfin saved the movie,
        // without new content. A refresh re-read it (DateLastRefreshed moved) but
        // Jellyfin did not save, so DateLastSaved stayed. It must not be re-read again.
        var saved = new DateTime(2026, 10, 1, 10, 5, 0, DateTimeKind.Utc);
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        File.WriteAllText(nfoPath, "<movie><title>Sample</title></movie>");
        File.SetLastWriteTimeUtc(nfoPath, saved.AddSeconds(20));
        var refreshed = new DateTime(2026, 11, 24, 11, 34, 8, DateTimeKind.Utc);

        var compareTo = MovieNfoRefresh.LibrarySavedUtc(saved, refreshed, DateTime.MinValue);

        Assert.True(MovieNfoRefresh.ShouldRefresh(nfoPath, saved));
        Assert.False(MovieNfoRefresh.ShouldRefresh(nfoPath, compareTo));
    }

    [Fact]
    public void FindExistingNfoPath_NoFile_ReturnsNull()
    {
        var moviePath = Path.Combine(_tempDirectory, "Movie.mkv");

        var result = MovieNfoParser.FindExistingNfoPath(moviePath);

        Assert.Null(result);
    }
}
