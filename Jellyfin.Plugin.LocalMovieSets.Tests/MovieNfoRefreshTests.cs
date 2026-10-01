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
    public void LibrarySavedUtc_PrefersDateLastSaved()
    {
        var saved = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = MovieNfoRefresh.LibrarySavedUtc(saved, modified);

        Assert.Equal(saved, result);
    }

    [Fact]
    public void FindExistingNfoPath_NoFile_ReturnsNull()
    {
        var moviePath = Path.Combine(_tempDirectory, "Movie.mkv");

        var result = MovieNfoParser.FindExistingNfoPath(moviePath);

        Assert.Null(result);
    }
}
