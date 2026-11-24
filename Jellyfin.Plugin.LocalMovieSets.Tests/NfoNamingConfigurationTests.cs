using Jellyfin.Plugin.LocalMovieSets;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class NfoNamingConfigurationTests
{
    [Fact]
    public void NfoNaming_FlatFile_IsRewrittenToSetSubfolder()
    {
        var config = new PluginConfiguration
        {
            NfoNaming = NfoNamingConvention.FlatFile
        };

        Assert.Equal(NfoNamingConvention.SetSubfolder, config.NfoNaming);
    }

    [Fact]
    public void NfoNaming_CollectionNfo_IsKept()
    {
        var config = new PluginConfiguration
        {
            NfoNaming = NfoNamingConvention.CollectionNfo
        };

        Assert.Equal(NfoNamingConvention.CollectionNfo, config.NfoNaming);
    }

    [Theory]
    [InlineData("SortName", "SortName")]
    [InlineData("PremiereDate", "PremiereDate")]
    [InlineData("Default", "PremiereDate")] // removed creation order
    [InlineData("Name", "PremiereDate")]    // legacy value
    [InlineData("", "PremiereDate")]
    public void CollectionSortBy_IsNormalized(string stored, string expected)
    {
        var config = new PluginConfiguration { CollectionSortBy = stored };

        Assert.Equal(expected, config.CollectionSortBy);
    }

    [Fact]
    public void CollectionSortBy_DefaultsToPremiereDate()
    {
        Assert.Equal("PremiereDate", new PluginConfiguration().CollectionSortBy);
    }
}
