using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class LibrarySelectionTests
{
    [Fact]
    public void IsNoneSelected_LegacyEmptyList_ScansAll()
    {
        Assert.False(LibrarySelection.IsNoneSelected([], true));
        Assert.False(LibrarySelection.IsNoneSelected(null, true));
    }

    [Fact]
    public void IsNoneSelected_SavedEmptyList_ScansNothing()
    {
        Assert.True(LibrarySelection.IsNoneSelected([], false));
    }

    [Fact]
    public void IsNoneSelected_ExplicitIds_ScansThoseLibraries()
    {
        Assert.False(LibrarySelection.IsNoneSelected(["lib-1"], false));
    }
}
