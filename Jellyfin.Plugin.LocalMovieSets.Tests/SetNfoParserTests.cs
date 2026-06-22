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
    [InlineData(NfoNamingConvention.SetSubfolder, "Marvel Cinematic Universe", @"Marvel Cinematic Universe\Marvel Cinematic Universe.nfo")]
    [InlineData(NfoNamingConvention.FlatFile, "Marvel Cinematic Universe", @"Marvel Cinematic Universe.nfo")]
    [InlineData(NfoNamingConvention.CollectionNfo, "Marvel Cinematic Universe", @"Marvel Cinematic Universe\collection.nfo")]
    public void GetNfoPath_ReturnsCorrectPathBasedOnConvention(NfoNamingConvention naming, string setName, string expectedRelativePath)
    {
        // Act
        var result = SetNfoParser.GetNfoPath(_tempDirectory, setName, naming);

        // Assert
        var expectedPath = Path.Combine(_tempDirectory, expectedRelativePath);
        Assert.Equal(expectedPath, result);
    }

    [Theory]
    [InlineData(NfoNamingConvention.SetSubfolder, "Marvel Cinematic Universe", @"Marvel Cinematic Universe")]
    [InlineData(NfoNamingConvention.FlatFile, "Marvel Cinematic Universe", "")]
    [InlineData(NfoNamingConvention.CollectionNfo, "Marvel Cinematic Universe", @"Marvel Cinematic Universe")]
    public void GetArtworkFolder_ReturnsCorrectFolderBasedOnConvention(NfoNamingConvention naming, string setName, string expectedRelativePath)
    {
        // Act
        var result = SetNfoParser.GetArtworkFolder(_tempDirectory, setName, naming);

        // Assert
        var expectedPath = string.IsNullOrEmpty(expectedRelativePath)
            ? _tempDirectory
            : Path.Combine(_tempDirectory, expectedRelativePath);
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
  <overview>The complete saga of Iron Man movies.</overview>
</set>";
        File.WriteAllText(nfoPath, nfoContent);

        // Act
        var result = _parser.ParseSet(_tempDirectory, setName, NfoNamingConvention.SetSubfolder);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Iron Man Collection", result.Title);
        Assert.Equal("Iron Man Original Collection", result.OriginalTitle);
        Assert.Equal("The complete saga of Iron Man movies.", result.Overview);
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
    }
}
