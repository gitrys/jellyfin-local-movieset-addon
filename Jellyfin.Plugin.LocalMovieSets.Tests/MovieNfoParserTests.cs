using System;
using System.IO;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class MovieNfoParserTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MovieNfoParser _parser;

    public MovieNfoParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalMovieSets_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _parser = new MovieNfoParser(NullLogger<MovieNfoParser>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void ParseNfo_NoNfoFile_ReturnsNullMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result.Membership);
        Assert.False(result.HadError);
    }

    [Fact]
    public void ParseNfo_NfoWithNoSetTag_ReturnsNullMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result.Membership);
        Assert.False(result.HadError);
    }

    [Fact]
    public void ParseNfo_NfoWithEmptySetName_ReturnsNullMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
  <set>
    <name></name>
  </set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result.Membership);
        Assert.False(result.HadError);
    }

    [Fact]
    public void ParseNfo_NfoWithWhitespaceName_ReturnsNullMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
  <set>
    <name>   </name>
  </set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result.Membership);
        Assert.False(result.HadError);
    }

    [Fact]
    public void ParseNfo_MalformedXml_ReportsError()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
  <set>
    <name>Some Collection</name>
  <!-- missing closing tags -->";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result.Membership);
        Assert.True(result.HadError);
    }

    [Fact]
    public void ParseNfo_ValidSetTag_ReturnsSetMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
  <set>
    <name>Marvel Cinematic Universe</name>
    <overview>A franchise of superhero movies.</overview>
  </set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.NotNull(result.Membership);
        Assert.False(result.HadError);
        Assert.Equal("Marvel Cinematic Universe", result.Membership.SetName);
        Assert.Equal("A franchise of superhero movies.", result.Membership.SetOverview);
    }

    [Fact]
    public void ParseNfo_SimpleSetTag_ReturnsSetMembership()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<movie>
  <title>Sample Movie</title>
  <set>Marvel Cinematic Universe</set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.NotNull(result.Membership);
        Assert.Equal("Marvel Cinematic Universe", result.Membership.SetName);
        Assert.Null(result.Membership.SetOverview);
    }

    [Fact]
    public void ParseNfo_TrailingJunkAfterRootElement_ReturnsSetMembership()
    {
        // Scrapers often append a trailer URL after the closing tag; Kodi accepts this.
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");
        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var nfoContent = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<movie>
  <title>Sample Movie</title>
  <set>
    <name>Sample Collection</name>
  </set>
</movie>
https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        File.WriteAllText(nfoPath, nfoContent);

        var result = _parser.ParseNfo(moviePath);

        Assert.NotNull(result.Membership);
        Assert.False(result.HadError);
        Assert.Equal("Sample Collection", result.Membership.SetName);
    }

    [Fact]
    public void ParseNfo_DirectoryMoviePath_FindsNfoInsideFolder()
    {
        // Folder-based rips (DVD/BD): the item path is the movie folder itself
        var movieDir = Path.Combine(_tempDirectory, "Iron Man (2008)");
        Directory.CreateDirectory(movieDir);

        var nfoPath = Path.Combine(movieDir, "Iron Man (2008).nfo");
        var nfoContent = @"<movie>
  <set>
    <name>Iron Man Collection</name>
  </set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        var result = _parser.ParseNfo(movieDir);

        Assert.NotNull(result.Membership);
        Assert.Equal("Iron Man Collection", result.Membership.SetName);
    }

    [Fact]
    public void ParseNfo_DirectoryMoviePath_FallsBackToMovieNfo()
    {
        var movieDir = Path.Combine(_tempDirectory, "Some Movie (1999)");
        Directory.CreateDirectory(movieDir);

        var nfoPath = Path.Combine(movieDir, "movie.nfo");
        var nfoContent = @"<movie>
  <set>
    <name>Some Collection</name>
  </set>
</movie>";
        File.WriteAllText(nfoPath, nfoContent);

        var result = _parser.ParseNfo(movieDir);

        Assert.NotNull(result.Membership);
        Assert.Equal("Some Collection", result.Membership.SetName);
    }

    [Fact]
    public void ParseNfo_MovieNamedNfoTakesPriority_ReturnsCorrectSet()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "ironman.mkv");

        // Movie-specific NFO
        var specificNfoPath = Path.Combine(_tempDirectory, "ironman.nfo");
        var specificNfoContent = @"<movie>
  <set>
    <name>Iron Man Collection</name>
  </set>
</movie>";
        File.WriteAllText(specificNfoPath, specificNfoContent);

        // Generic movie.nfo (should be bypassed/ignored because specific takes priority)
        var genericNfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        var genericNfoContent = @"<movie>
  <set>
    <name>Generic Collection</name>
  </set>
</movie>";
        File.WriteAllText(genericNfoPath, genericNfoContent);

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.NotNull(result.Membership);
        Assert.Equal("Iron Man Collection", result.Membership.SetName);
    }
}
