using System;
using System.IO;
using Jellyfin.Plugin.LocalMovieSets;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class SetNfoParserTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SetNfoParser _parser;

    public SetNfoParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalMovieSets_SetTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _parser = new SetNfoParser(NullLogger<SetNfoParser>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Theory]
    [InlineData(NfoNamingConvention.SetSubfolder)]
    [InlineData(NfoNamingConvention.FlatFile)]
    [InlineData(NfoNamingConvention.CollectionNfo)]
    public void GetNfoPath_ReturnsCorrectPathBasedOnConvention(NfoNamingConvention naming)
    {
        const string setName = "Marvel Cinematic Universe";
        var safeName = SetNfoParser.SanitizeFolderName(setName);

        // Act
        var result = SetNfoParser.GetNfoPath(_tempDirectory, setName, naming);

        // Assert — build expected path with Path.Combine so tests pass on Linux CI too
        var expectedPath = naming switch
        {
            NfoNamingConvention.SetSubfolder => Path.Combine(_tempDirectory, safeName, $"{safeName}.nfo"),
            NfoNamingConvention.FlatFile => Path.Combine(_tempDirectory, $"{safeName}.nfo"),
            NfoNamingConvention.CollectionNfo => Path.Combine(_tempDirectory, safeName, "collection.nfo"),
            _ => throw new ArgumentOutOfRangeException(nameof(naming))
        };
        Assert.Equal(expectedPath, result);
    }

    [Theory]
    [InlineData(NfoNamingConvention.SetSubfolder)]
    [InlineData(NfoNamingConvention.FlatFile)]
    [InlineData(NfoNamingConvention.CollectionNfo)]
    public void GetArtworkFolder_ReturnsCorrectFolderBasedOnConvention(NfoNamingConvention naming)
    {
        const string setName = "Marvel Cinematic Universe";
        var safeName = SetNfoParser.SanitizeFolderName(setName);

        // Act
        var result = SetNfoParser.GetArtworkFolder(_tempDirectory, setName, naming);

        // Assert
        var expectedPath = naming == NfoNamingConvention.FlatFile
            ? _tempDirectory
            : Path.Combine(_tempDirectory, safeName);
        Assert.Equal(expectedPath, result);
    }

    [Theory]
    [InlineData("Marvel: MCU", "Marvel MCU")]
    [InlineData("Alien / Predator", "Alien  Predator")]
    [InlineData("Back to the Future*", "Back to the Future")]
    [InlineData("Test?<>|:\"*\\/", "Test")]
    public void SanitizeFolderName_RemovesInvalidCharacters(string input, string expected)
    {
        // Act
        var result = SetNfoParser.SanitizeFolderName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseSet_FolderNotExists_ReturnsNull()
    {
        // Arrange
        var nonExistentFolder = Path.Combine(_tempDirectory, "nonexistent");

        // Act
        var result = _parser.ParseSet(nonExistentFolder, "MCU", NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseSet_NfoNotExists_ReturnsNull()
    {
        // Act
        var result = _parser.ParseSet(_tempDirectory, "MCU", NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseSet_ValidNfo_ReturnsSetNfoInfo()
    {
        // Arrange
        var setName = "Iron Man Collection";
        var subfolder = Path.Combine(_tempDirectory, setName);
        Directory.CreateDirectory(subfolder);

        var nfoPath = Path.Combine(subfolder, $"{setName}.nfo");
        var nfoContent = @"<set>
  <title>Iron Man Collection</title>
  <originaltitle>Iron Man Original Collection</originaltitle>
  <plot>The complete saga of Iron Man movies.</plot>
  <genre>Action</genre>
  <genre>Sci-Fi</genre>
  <studio>Marvel Studios</studio>
  <tmdbid>125570</tmdbid>
  <imdbid>tt0123456</imdbid>
</set>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseSet(_tempDirectory, setName, NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Iron Man Collection", result.Title);
        Assert.Equal("Iron Man Original Collection", result.OriginalTitle);
        Assert.Equal("The complete saga of Iron Man movies.", result.Overview);
        Assert.Equal(2, result.Genres.Count);
        Assert.Contains("Action", result.Genres);
        Assert.Contains("Sci-Fi", result.Genres);
        Assert.Single(result.Studios);
        Assert.Equal("Marvel Studios", result.Studios[0]);
        Assert.Equal("125570", result.TmdbId);
        Assert.Equal("tt0123456", result.ImdbId);
    }

    [Fact]
    public void ParseSet_EmptyTitleInNfo_FallbackToSetName()
    {
        // Arrange
        var setName = "Iron Man Collection";
        var subfolder = Path.Combine(_tempDirectory, setName);
        Directory.CreateDirectory(subfolder);

        var nfoPath = Path.Combine(subfolder, $"{setName}.nfo");
        var nfoContent = @"<set>
  <title></title>
  <overview>Saga description.</overview>
</set>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseSet(_tempDirectory, setName, NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(setName, result.Title);
        Assert.Equal("Saga description.", result.Overview);
        Assert.Empty(result.Genres);
        Assert.Empty(result.Studios);
    }

    [Fact]
    public void ParseSet_UniqueId_ReturnsSetNfoInfo()
    {
        // Arrange
        var setName = "Iron Man Collection";
        var subfolder = Path.Combine(_tempDirectory, setName);
        Directory.CreateDirectory(subfolder);

        var nfoPath = Path.Combine(subfolder, $"{setName}.nfo");
        var nfoContent = @"<set>
  <title>Iron Man Collection</title>
  <uniqueid default=""true"" type=""tmdb"">125570</uniqueid>
  <uniqueid type=""imdb"">tt0123456</uniqueid>
</set>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseSet(_tempDirectory, setName, NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("125570", result.TmdbId);
        Assert.Equal("tt0123456", result.ImdbId);
    }
}
