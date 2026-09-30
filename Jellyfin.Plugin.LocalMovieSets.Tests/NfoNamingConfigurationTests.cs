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
}
