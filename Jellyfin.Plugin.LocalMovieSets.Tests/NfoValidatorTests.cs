using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Services.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class NfoValidatorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly NfoValidator _validator;

    public NfoValidatorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalMovieSets_ValTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _validator = new NfoValidator(NullLogger<NfoValidator>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void Validate_ReturnsHealthy_WhenSetHasValidNfoAndArtwork()
    {
        // Arrange
        var setFolder = Path.Combine(_tempDirectory, "Batman Collection");
        Directory.CreateDirectory(setFolder);

        File.WriteAllText(Path.Combine(setFolder, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection>
  <title>Batman Collection</title>
  <plot>The Dark Knight film series.</plot>
</collection>");
        File.WriteAllText(Path.Combine(setFolder, "poster.jpg"), "dummy poster");
        File.WriteAllText(Path.Combine(setFolder, "fanart.jpg"), "dummy fanart");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.Equal(1, report.ScannedSetsCount);
        Assert.Equal(1, report.HealthySetsCount);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Validate_ReturnsError_WhenNfoXmlIsMalformed()
    {
        // Arrange
        var setFolder = Path.Combine(_tempDirectory, "Broken XML Set");
        Directory.CreateDirectory(setFolder);

        File.WriteAllText(Path.Combine(setFolder, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection>
  <title>Broken Set</title>
  <unclosed>tag mismatch
</collection>");
        File.WriteAllText(Path.Combine(setFolder, "poster.jpg"), "dummy");
        File.WriteAllText(Path.Combine(setFolder, "fanart.jpg"), "dummy");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.True(report.ErrorCount >= 1);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Error && i.Category == ValidationCategory.XmlSyntax);
        var xmlIssue = report.Issues.First(i => i.Category == ValidationCategory.XmlSyntax);
        Assert.NotNull(xmlIssue.LineNumber);
    }

    [Fact]
    public void Validate_ReturnsError_WhenNfoIsEmpty()
    {
        // Arrange
        var setFolder = Path.Combine(_tempDirectory, "Empty NFO Set");
        Directory.CreateDirectory(setFolder);

        File.WriteAllText(Path.Combine(setFolder, "collection.nfo"), string.Empty);
        File.WriteAllText(Path.Combine(setFolder, "poster.jpg"), "dummy");
        File.WriteAllText(Path.Combine(setFolder, "fanart.jpg"), "dummy");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.True(report.ErrorCount >= 1);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Error && i.Category == ValidationCategory.EmptyFile);
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenArtworkIsMissing()
    {
        // Arrange
        var setFolder = Path.Combine(_tempDirectory, "No Artwork Set");
        Directory.CreateDirectory(setFolder);

        File.WriteAllText(Path.Combine(setFolder, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection>
  <title>No Artwork Set</title>
</collection>");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.Equal(0, report.ErrorCount);
        Assert.True(report.WarningCount >= 1);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Warning && i.Category == ValidationCategory.MissingArtwork);
    }

    [Fact]
    public void Validate_ReturnsInfo_WhenFolderHasNoNfo()
    {
        // Arrange
        var emptyFolder = Path.Combine(_tempDirectory, "Random Empty Folder");
        Directory.CreateDirectory(emptyFolder);
        File.WriteAllText(Path.Combine(emptyFolder, "movie.mkv"), "dummy video");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.True(report.InfoCount >= 1);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Info && i.Category == ValidationCategory.FolderWithoutNfo);
    }

    [Fact]
    public void Validate_ReturnsInfo_WhenNonStandardCoverIsFound()
    {
        // Arrange
        var setFolder = Path.Combine(_tempDirectory, "Cover Naming Set");
        Directory.CreateDirectory(setFolder);

        File.WriteAllText(Path.Combine(setFolder, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection>
  <title>Cover Naming Set</title>
</collection>");
        File.WriteAllText(Path.Combine(setFolder, "cover.jpg"), "cover dummy");
        File.WriteAllText(Path.Combine(setFolder, "fanart.jpg"), "fanart dummy");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Info && i.Category == ValidationCategory.NonStandardArtwork);
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenDuplicateCollectionNamesExist()
    {
        // Arrange
        var folder1 = Path.Combine(_tempDirectory, "Set A");
        var folder2 = Path.Combine(_tempDirectory, "Set B");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        File.WriteAllText(Path.Combine(folder1, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection><title>Duplicate Name</title></collection>");
        File.WriteAllText(Path.Combine(folder1, "poster.jpg"), "dummy");
        File.WriteAllText(Path.Combine(folder1, "fanart.jpg"), "dummy");

        File.WriteAllText(Path.Combine(folder2, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection><title>Duplicate Name</title></collection>");
        File.WriteAllText(Path.Combine(folder2, "poster.jpg"), "dummy");
        File.WriteAllText(Path.Combine(folder2, "fanart.jpg"), "dummy");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Warning && i.Category == ValidationCategory.DuplicateName);
    }

    [Fact]
    public void Validate_DetectsThemeSong_WhenPresentInFolder()
    {
        // Arrange
        var folder = Path.Combine(_tempDirectory, "The Matrix Collection");
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "collection.nfo"), @"<?xml version=""1.0"" encoding=""UTF-8""?>
<collection><title>The Matrix Collection</title></collection>");
        File.WriteAllText(Path.Combine(folder, "poster.jpg"), "dummy poster");
        File.WriteAllText(Path.Combine(folder, "fanart.jpg"), "dummy fanart");
        File.WriteAllText(Path.Combine(folder, "theme.mp3"), "dummy theme music bytes");

        // Act
        var report = _validator.Validate(_tempDirectory);

        // Assert
        Assert.Contains(report.Issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Category == ValidationCategory.ThemeSong
            && i.Message.Contains("theme.mp3"));
    }
}
