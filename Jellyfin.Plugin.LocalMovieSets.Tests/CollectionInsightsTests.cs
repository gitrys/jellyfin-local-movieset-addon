using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class CollectionInsightsTests
{
    [Fact]
    public void ComputeInsights_WithEmptyGroups_ReturnsZeroAndNulls()
    {
        // Act
        var result = LocalMovieSetManager.ComputeInsights(new Dictionary<string, List<Movie>>());

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.LargestCollectionName);
        Assert.Equal(0, result.LargestCollectionMovieCount);
        Assert.Null(result.OldestFranchiseName);
        Assert.Null(result.OldestFranchiseYear);
        Assert.Null(result.NewestFranchiseName);
        Assert.Null(result.NewestFranchiseYear);
        Assert.Null(result.TopRatedCollectionName);
        Assert.Null(result.TopRatedAverageRating);
        Assert.Equal(0, result.AverageMoviesPerSet);
    }

    [Fact]
    public void ComputeInsights_CalculatesMetricsCorrectly()
    {
        // Arrange
        // Franchise 1: James Bond (3 movies, oldest 1962, newest 1964, ratings 7.0, 8.0, 9.0 -> avg 8.0)
        var bond1 = new Movie { Name = "Dr. No", ProductionYear = 1962, CommunityRating = 7.0f };
        var bond2 = new Movie { Name = "From Russia with Love", ProductionYear = 1963, CommunityRating = 8.0f };
        var bond3 = new Movie { Name = "Goldfinger", ProductionYear = 1964, CommunityRating = 9.0f };

        // Franchise 2: Avatar (2 movies, oldest 2009, newest 2022, ratings 8.5, 9.5 -> avg 9.0)
        var avatar1 = new Movie { Name = "Avatar", ProductionYear = 2009, CommunityRating = 8.5f };
        var avatar2 = new Movie { Name = "Avatar: The Way of Water", ProductionYear = 2022, CommunityRating = 9.5f };

        // Franchise 3: King Kong (1 movie, oldest 1933, rating 8.0)
        var kong1 = new Movie { Name = "King Kong", ProductionYear = 1933, CommunityRating = 8.0f };

        var groups = new Dictionary<string, List<Movie>>(StringComparer.OrdinalIgnoreCase)
        {
            ["James Bond Collection"] = new List<Movie> { bond1, bond2, bond3 },
            ["Avatar Collection"] = new List<Movie> { avatar1, avatar2 },
            ["King Kong Collection"] = new List<Movie> { kong1 }
        };

        // Act
        var result = LocalMovieSetManager.ComputeInsights(groups);

        // Assert
        // 1. Largest
        Assert.Equal("James Bond Collection", result.LargestCollectionName);
        Assert.Equal(3, result.LargestCollectionMovieCount);

        // 2. Oldest
        Assert.Equal("King Kong Collection", result.OldestFranchiseName);
        Assert.Equal(1933, result.OldestFranchiseYear);

        // 3. Newest
        Assert.Equal("Avatar Collection", result.NewestFranchiseName);
        Assert.Equal(2022, result.NewestFranchiseYear);

        // 4. Top Rated (Avatar: 9.0 vs Bond: 8.0)
        Assert.Equal("Avatar Collection", result.TopRatedCollectionName);
        Assert.Equal(9.0f, result.TopRatedAverageRating);

        // 5. Average movies per set: 6 total movies / 3 sets = 2.0
        Assert.Equal(2.0, result.AverageMoviesPerSet);
    }
}
