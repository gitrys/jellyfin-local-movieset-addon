using Jellyfin.Plugin.LocalMovieSets.Providers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class ImageFetcherCheckTests
{
    [Fact]
    public void Evaluate_OnlyLocalMovieSets_HasNoConflict()
    {
        var result = ImageFetcherCheck.Evaluate(true, [BoxSetImageProvider.ProviderName]);

        Assert.False(result.HasConflict);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void Evaluate_LocalNameDiffersByCase_HasNoConflict()
    {
        var result = ImageFetcherCheck.Evaluate(true, ["local movie sets"]);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public void Evaluate_ExtraFetcher_HasConflict()
    {
        var result = ImageFetcherCheck.Evaluate(true, [BoxSetImageProvider.ProviderName, "TheMovieDb"]);

        Assert.True(result.HasConflict);
        Assert.Equal(ImageFetcherCheck.NotOnlyLocalMovieSets, result.Reason);
        Assert.Equal([BoxSetImageProvider.ProviderName, "TheMovieDb"], result.EnabledFetchers);
    }

    [Fact]
    public void Evaluate_MissingLocalFetcher_HasConflict()
    {
        var result = ImageFetcherCheck.Evaluate(true, ["TheMovieDb"]);

        Assert.True(result.HasConflict);
        Assert.Equal(ImageFetcherCheck.NotOnlyLocalMovieSets, result.Reason);
    }

    [Fact]
    public void Evaluate_EmptyList_HasConflict()
    {
        var result = ImageFetcherCheck.Evaluate(true, []);

        Assert.True(result.HasConflict);
        Assert.Equal(ImageFetcherCheck.NotOnlyLocalMovieSets, result.Reason);
    }

    [Fact]
    public void Evaluate_NoBoxSetOptions_HasConflict()
    {
        var result = ImageFetcherCheck.Evaluate(false, [BoxSetImageProvider.ProviderName]);

        Assert.True(result.HasConflict);
        Assert.Equal(ImageFetcherCheck.NoBoxSetOptions, result.Reason);
        Assert.Empty(result.EnabledFetchers);
    }

    [Fact]
    public void Combine_OtherMetadataDownloader_HasConflict()
    {
        var images = ImageFetcherCheck.Evaluate(true, [BoxSetImageProvider.ProviderName]);
        var metadata = ImageFetcherCheck.EvaluateMetadataDownloaders(true, ["TheMovieDb"]);

        var result = ImageFetcherCheck.Combine(images, metadata);

        Assert.True(result.HasConflict);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Equal(ImageFetcherCheck.NotOnlyLocalMovieSets, result.MetadataReason);
        Assert.Equal(["TheMovieDb"], result.EnabledMetadataFetchers);
    }

    [Fact]
    public void Combine_EmptyMetadataDownloaders_HasNoConflict()
    {
        var images = ImageFetcherCheck.Evaluate(true, [BoxSetImageProvider.ProviderName]);
        var metadata = ImageFetcherCheck.EvaluateMetadataDownloaders(true, []);

        var result = ImageFetcherCheck.Combine(images, metadata);

        Assert.False(result.HasConflict);
    }
}
