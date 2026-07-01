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
    [InlineData("Default", "")]
    [InlineData("SortName Descending", "")] // legacy config value
    [InlineData("", "")]
    public void MapSortByToJellyfin_MapsOnlySupportedValues(string input, string expected)
    {
        Assert.Equal(expected, BoxSetMetadataProvider.MapSortByToJellyfin(input));
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
}
