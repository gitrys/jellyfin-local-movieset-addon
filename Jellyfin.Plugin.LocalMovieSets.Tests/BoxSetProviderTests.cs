using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class BoxSetProviderTests
{
    [Theory]
    [InlineData("SortName", "SortName")]
    [InlineData("PremiereDate", "PremiereDate")]
    [InlineData("Default", "PremiereDate")] // removed creation order
    [InlineData("SortName Descending", "PremiereDate")] // legacy config value
    [InlineData("", "PremiereDate")]
    [InlineData(null, "PremiereDate")]
    public void MapSortByToJellyfin_MapsToSupportedValues(string? input, string expected)
    {
        Assert.Equal(expected, BoxSetMetadataProvider.MapSortByToJellyfin(input));
    }

    [Theory]
    [InlineData(true, "SortName", true)]       // new or first adopted collection
    [InlineData(false, "", true)]              // empty: old "Creation Order" setting or editor reset
    [InlineData(false, null, true)]
    [InlineData(false, "SortName", false)]     // chosen in the metadata editor: keep
    [InlineData(false, "PremiereDate", false)]
    [InlineData(false, "Default", false)]      // editor option "Date modified" (unsorted): keep
    public void ShouldApplyDisplayOrder_OnlyForNewOrEmpty(bool newlyOwned, string? current, bool expected)
    {
        Assert.Equal(expected, BoxSetMetadataProvider.ShouldApplyDisplayOrder(newlyOwned, current));
    }


    [Fact]
    public void ApplySortTitle_UnchangedRawTitle_IsNotAChange()
    {
        // SortName would return Jellyfin's normalized form; the raw stored
        // value is ForcedSortName, so an unchanged title must not count as changed.
        var collection = new MediaBrowser.Controller.Entities.Movies.BoxSet { ForcedSortName = "Alien 1" };

        Assert.False(BoxSetMetadataProvider.ApplySortTitle(collection, "Alien 1"));
        Assert.Equal("Alien 1", collection.ForcedSortName);
    }

    [Fact]
    public void ApplySortTitle_NewTitle_IsApplied()
    {
        var collection = new MediaBrowser.Controller.Entities.Movies.BoxSet { ForcedSortName = "Alien 1" };

        Assert.True(BoxSetMetadataProvider.ApplySortTitle(collection, "Alien Saga"));
        Assert.Equal("Alien Saga", collection.ForcedSortName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplySortTitle_EmptyTitle_KeepsCurrentValue(string? sortTitle)
    {
        var collection = new MediaBrowser.Controller.Entities.Movies.BoxSet { ForcedSortName = "Alien 1" };

        Assert.False(BoxSetMetadataProvider.ApplySortTitle(collection, sortTitle));
        Assert.Equal("Alien 1", collection.ForcedSortName);
    }

    [Fact]
    public void ArtworkMappings_CoverAllExpectedImageTypes()
    {
        var types = BoxSetImageProvider.ArtworkMappings.Select(m => m.ImageType).ToList();

        Assert.Equal(types.Count, types.Distinct().Count());
        Assert.Contains(ImageType.Primary, types);
        Assert.Contains(ImageType.Backdrop, types);
        Assert.Contains(ImageType.Logo, types);
        Assert.Contains(ImageType.Thumb, types);
        Assert.Contains(ImageType.Art, types);
        Assert.Contains(ImageType.Banner, types);
        Assert.Contains(ImageType.Disc, types);
        Assert.Contains(ImageType.Box, types);
        Assert.Contains(ImageType.BoxRear, types);
        Assert.Contains(ImageType.Menu, types);
    }

    [Fact]
    public void ArtworkMappings_PosterIsFirstPrimaryCandidate()
    {
        var primary = BoxSetImageProvider.ArtworkMappings.Single(m => m.ImageType == ImageType.Primary);
        Assert.Equal("poster.jpg", primary.FileNames[0]);
    }

    [Fact]
    public void GetMovieFolderCandidateFileNames_Primary_ContainsExpectedPatterns()
    {
        var candidates = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Iron Man Collection", ImageType.Primary);

        Assert.Contains("movieset-poster.jpg", candidates);
        Assert.Contains("movieset-poster.png", candidates);
        Assert.Contains("Iron Man Collection-poster.jpg", candidates);
        Assert.Contains("movieset-folder.jpg", candidates);
        Assert.Contains("Iron Man Collection-folder.png", candidates);
    }

    [Fact]
    public void GetMovieFolderCandidateFileNames_SpecialCharacters_GeneratesSanitizedVariants()
    {
        var candidates = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Star Wars: Collection", ImageType.Primary);

        // tinyMediaManager replaces ':' with '_' by default or space
        Assert.Contains("Star Wars_ Collection-poster.jpg", candidates);
        Assert.Contains("Star Wars  Collection-poster.jpg", candidates);
        Assert.Contains("Star Wars Collection-poster.jpg", candidates);
    }

    [Fact]
    public void GetMovieFolderCandidateFileNames_AllArtworkTypes_Covered()
    {
        var backdrop = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Backdrop);
        Assert.Contains("movieset-fanart.jpg", backdrop);
        Assert.Contains("movieset-backdrop.jpg", backdrop);

        var logo = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Logo);
        Assert.Contains("movieset-clearlogo.png", logo);
        Assert.Contains("movieset-logo.png", logo);

        var thumb = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Thumb);
        Assert.Contains("movieset-thumb.jpg", thumb);
        Assert.Contains("movieset-landscape.jpg", thumb);

        var art = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Art);
        Assert.Contains("movieset-clearart.png", art);

        var banner = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Banner);
        Assert.Contains("movieset-banner.jpg", banner);

        var disc = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Matrix", ImageType.Disc);
        Assert.Contains("movieset-disc.png", disc);
        Assert.Contains("movieset-discart.png", disc);
    }

    [Fact]
    public void FindFirstExistingFile_ReturnsFirstMatchInPriority()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lms_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var poster1 = System.IO.Path.Combine(tempDir, "movieset-folder.jpg");
            var poster2 = System.IO.Path.Combine(tempDir, "movieset-poster.jpg");
            System.IO.File.WriteAllText(poster1, "dummy1");
            System.IO.File.WriteAllText(poster2, "dummy2");

            var candidates = BoxSetImageProvider.GetMovieFolderCandidateFileNames("Test", ImageType.Primary);
            var match = BoxSetImageProvider.FindFirstExistingFile(tempDir, candidates);

            // movieset-poster.jpg has higher priority than movieset-folder.jpg
            Assert.Equal(poster2, match);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }
}
