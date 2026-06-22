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
    public void ParseNfo_NoNfoFile_ReturnsNull()
    {
        // Arrange
        var moviePath = Path.Combine(_tempDirectory, "movie.mkv");

        // Act
        var result = _parser.ParseNfo(moviePath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseNfo_NfoWithNoSetTag_ReturnsNull()
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
        Assert.Null(result);
    }

    [Fact]
    public void ParseNfo_NfoWithEmptySetName_ReturnsNull()
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
        Assert.Null(result);
    }

    [Fact]
    public void ParseNfo_NfoWithWhitespaceName_ReturnsNull()
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
        Assert.Null(result);
    }

    [Fact]
    public void ParseNfo_MalformedXml_ReturnsNull()
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
        Assert.Null(result);
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
        Assert.NotNull(result);
        Assert.Equal("Marvel Cinematic Universe", result.SetName);
        Assert.Equal("A franchise of superhero movies.", result.SetOverview);
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
        Assert.NotNull(result);
        Assert.Equal("Iron Man Collection", result.SetName);
    }
}
