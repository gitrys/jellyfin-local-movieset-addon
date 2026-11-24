using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Services.Validation;
using Jellyfin.Plugin.LocalMovieSets;
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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

        // Assert
        Assert.True(report.InfoCount >= 1);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Info && i.Category == ValidationCategory.FolderWithoutNfo);
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenNonStandardCoverIsFound()
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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

        // Assert
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Warning && i.Category == ValidationCategory.NonStandardArtwork);
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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

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
        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.CollectionNfo);

        // Assert
        Assert.Contains(report.Issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Category == ValidationCategory.ThemeSong
            && i.Message.Contains("theme.mp3"));
    }

    // ── Same rules as the sync (#8) ──────────────────────────────────────

    private const string SetNfo = @"<?xml version=""1.0"" encoding=""UTF-8""?><set><title>Alien Collection</title></set>";

    private string CreateSet(string name, params string[] files)
    {
        var folder = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), file.EndsWith(".nfo", StringComparison.Ordinal) ? SetNfo : "dummy");
        }

        return folder;
    }

    [Fact]
    public void SetSubfolder_WithMatchingNfoAndArtwork_IsHealthy()
    {
        CreateSet("Alien Collection", "Alien Collection.nfo", "poster.jpg", "fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        Assert.Equal(1, report.HealthySetsCount);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void NfoWithOtherName_IsNamingMismatchError()
    {
        CreateSet("Alien Collection", "alien.nfo", "poster.jpg", "fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(ValidationCategory.NamingMismatch, issue.Category);
        Assert.Contains("Alien Collection.nfo", issue.Message, StringComparison.Ordinal);
        Assert.Equal(0, report.HealthySetsCount);
    }

    [Fact]
    public void CollectionNfo_UnderSetSubfolderNaming_IsNamingMismatch()
    {
        CreateSet("Alien Collection", "collection.nfo", "poster.jpg", "fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        Assert.Contains(report.Issues, i => i.Category == ValidationCategory.NamingMismatch);
    }

    [Fact]
    public void OnlyTheExpectedNfo_IsValidated()
    {
        var folder = CreateSet("Alien Collection", "Alien Collection.nfo", "poster.jpg", "fanart.jpg");
        File.WriteAllText(Path.Combine(folder, "notes.nfo"), "<broken");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void NfoInDataFolderRoot_IsUnsupportedLayoutError()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "Alien Collection.nfo"), SetNfo);
        File.WriteAllText(Path.Combine(_tempDirectory, "Alien Collection-poster.jpg"), "dummy");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(ValidationCategory.UnsupportedLayout, issue.Category);
        Assert.Equal(1, report.ScannedSetsCount);
        Assert.Equal(0, report.HealthySetsCount);
    }

    [Fact]
    public void NestedSetFolders_AreReportedOnce_AndNotWalked()
    {
        // Universe/Part 1/Part 1.nfo and three more levels below
        var deep = Path.Combine(_tempDirectory, "Universe", "Part 1", "Level 3", "Level 4");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(_tempDirectory, "Universe", "Part 1", "Part 1.nfo"), SetNfo);
        File.WriteAllText(Path.Combine(deep, "Level 4.nfo"), "<broken");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(ValidationCategory.UnsupportedLayout, issue.Category);
        Assert.Equal("Universe", issue.CollectionName);
    }

    [Fact]
    public void MoviesetPosterInSetFolder_IsNotCountedAsPoster()
    {
        CreateSet("Alien Collection", "Alien Collection.nfo", "movieset-poster.jpg", "fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(ValidationCategory.NonStandardArtwork, issue.Category);
        Assert.Contains("movieset-poster.jpg", issue.Message, StringComparison.Ordinal);
        Assert.Equal(0, report.HealthySetsCount);
    }

    [Fact]
    public void MoviesetFanartInSetFolder_IsNotCountedAsBackdrop()
    {
        CreateSet("Alien Collection", "Alien Collection.nfo", "poster.jpg", "movieset-fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Info, issue.Severity);
        Assert.Equal(ValidationCategory.NonStandardArtwork, issue.Category);
    }

    [Theory]
    [InlineData("Alien Collection-theme.mp3")]
    [InlineData("Alien Collection.theme.flac")]
    [InlineData("theme.ogg")]
    public void ThemeSong_IsFoundWithTheSyncsFileNames(string themeFile)
    {
        CreateSet("Alien Collection", "Alien Collection.nfo", "poster.jpg", "fanart.jpg", themeFile);

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationCategory.ThemeSong, issue.Category);
        Assert.Contains(themeFile, issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("theme.mp4")]
    [InlineData("Alien Collection-theme.mkv")]
    [InlineData("Alien Collection.theme.webm")]
    public void ThemeVideo_IsFoundWithTheSyncsFileNames(string videoFile)
    {
        CreateSet("Alien Collection", "Alien Collection.nfo", "poster.jpg", "fanart.jpg", videoFile);

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationSeverity.Info, issue.Severity);
        Assert.Equal(ValidationCategory.ThemeVideo, issue.Category);
        Assert.Contains(videoFile, issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderWithoutNfo_StillGetsArtworkCheck()
    {
        CreateSet("Alien Collection", "fanart.jpg");

        var report = _validator.Validate(_tempDirectory, NfoNamingConvention.SetSubfolder);

        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Info && i.Category == ValidationCategory.FolderWithoutNfo);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Warning && i.Category == ValidationCategory.MissingArtwork);
    }
}
