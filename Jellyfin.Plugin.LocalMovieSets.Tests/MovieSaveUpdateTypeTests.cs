using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class MovieSaveUpdateTypeTests
{
    [Fact]
    public void MovieSaveUpdateType_StaysBelowSaverThreshold()
    {
        // Jellyfin's movie NFO saver writes the file for ImageUpdate and above.
        // The plugin's own movie saves must stay below, or the user's NFOs get rewritten.
        Assert.True(LocalMovieSetManager.MovieSaveUpdateType < ItemUpdateType.ImageUpdate);
        Assert.NotEqual(ItemUpdateType.MetadataEdit, LocalMovieSetManager.MovieSaveUpdateType);
    }
}
