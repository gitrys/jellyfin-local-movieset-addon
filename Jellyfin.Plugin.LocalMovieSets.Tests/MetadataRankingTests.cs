using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class MetadataRankingTests
{
    [Fact]
    public void RankStringsByFrequency_OrdersByCountDescending_AndRespectsLimit()
    {
        // Simulate Alien movies
        var movie1Tags = new[] { "alien", "space", "xenomorph", "nostromo" };
        var movie2Tags = new[] { "alien", "space", "xenomorph", "marines" };
        var movie3Tags = new[] { "alien", "space", "fury 161" };
        var movie4Tags = new[] { "alien", "cloning" };

        var sources = new[] { movie1Tags, movie2Tags, movie3Tags, movie4Tags };

        // Top 3 tags
        var top3 = BoxSetMetadataProvider.RankStringsByFrequency(sources, 3);

        Assert.Equal(3, top3.Count);
        Assert.Equal("alien", top3[0]);     // 4 movies
        Assert.Equal("space", top3[1]);     // 3 movies
        Assert.Equal("xenomorph", top3[2]); // 2 movies
    }

    [Fact]
    public void RankStringsByFrequency_LimitZero_ReturnsAllRanked()
    {
        var m1 = new[] { "Sci-Fi", "Horror" };
        var m2 = new[] { "Sci-Fi", "Action" };
        var m3 = new[] { "Sci-Fi", "Thriller" };

        var result = BoxSetMetadataProvider.RankStringsByFrequency(new[] { m1, m2, m3 }, 0);

        Assert.Equal(4, result.Count);
        Assert.Equal("Sci-Fi", result[0]); // 3 movies
        // "Action", "Horror", "Thriller" each have count 1, sorted alphabetically
        Assert.Equal("Action", result[1]);
        Assert.Equal("Horror", result[2]);
        Assert.Equal("Thriller", result[3]);
    }

    [Fact]
    public void RankStringsByFrequency_DeduplicatesWithinSingleMovie()
    {
        // A single movie listing the same tag multiple times should only count once
        var m1 = new[] { "action", "Action", "ACTION" };
        var m2 = new[] { "Action" };

        var result = BoxSetMetadataProvider.RankStringsByFrequency(new[] { m1, m2 }, 0);

        Assert.Single(result);
        Assert.Equal("action", result[0], ignoreCase: true);
    }

    [Fact]
    public void RankPeopleByFrequency_Directors_RanksByMovieCount()
    {
        // Simulate Bond directors
        var m1 = new[] { new PersonInfo { Name = "John Glen", Type = PersonKind.Director } };
        var m2 = new[] { new PersonInfo { Name = "John Glen", Type = PersonKind.Director } };
        var m3 = new[] { new PersonInfo { Name = "John Glen", Type = PersonKind.Director } };
        var m4 = new[] { new PersonInfo { Name = "Guy Hamilton", Type = PersonKind.Director } };
        var m5 = new[] { new PersonInfo { Name = "Guy Hamilton", Type = PersonKind.Director } };
        var m6 = new[] { new PersonInfo { Name = "Martin Campbell", Type = PersonKind.Director } };

        var sources = new[] { m1, m2, m3, m4, m5, m6 };

        var topDirectors = BoxSetMetadataProvider.RankPeopleByFrequency(sources, PersonKind.Director, 0, 2);

        Assert.Equal(2, topDirectors.Count);
        Assert.Equal("John Glen", topDirectors[0].Name);     // 3 movies
        Assert.Equal("Guy Hamilton", topDirectors[1].Name);  // 2 movies
    }

    [Fact]
    public void RankPeopleByFrequency_Actors_RanksRecurringCastFirst()
    {
        var m1 = new[]
        {
            new PersonInfo { Name = "Sigourney Weaver", Type = PersonKind.Actor },
            new PersonInfo { Name = "Tom Skerritt", Type = PersonKind.Actor },
            new PersonInfo { Name = "John Hurt", Type = PersonKind.Actor }
        };
        var m2 = new[]
        {
            new PersonInfo { Name = "Sigourney Weaver", Type = PersonKind.Actor },
            new PersonInfo { Name = "Michael Biehn", Type = PersonKind.Actor },
            new PersonInfo { Name = "Lance Henriksen", Type = PersonKind.Actor }
        };
        var m3 = new[]
        {
            new PersonInfo { Name = "Sigourney Weaver", Type = PersonKind.Actor },
            new PersonInfo { Name = "Charles S. Dutton", Type = PersonKind.Actor },
            new PersonInfo { Name = "Lance Henriksen", Type = PersonKind.Actor }
        };
        var m4 = new[]
        {
            new PersonInfo { Name = "Sigourney Weaver", Type = PersonKind.Actor },
            new PersonInfo { Name = "Winona Ryder", Type = PersonKind.Actor }
        };

        var sources = new[] { m1, m2, m3, m4 };

        // Take top 2 recurring actors across all movies
        var topActors = BoxSetMetadataProvider.RankPeopleByFrequency(sources, PersonKind.Actor, 0, 2);

        Assert.Equal(2, topActors.Count);
        Assert.Equal("Sigourney Weaver", topActors[0].Name);  // 4 movies
        Assert.Equal("Lance Henriksen", topActors[1].Name);   // 2 movies
    }

    [Fact]
    public void ResolveCollectionStringList_AggregateOn_UsesMovies_IgnoresSetNfo()
    {
        var movies = new[]
        {
            new[] { "Sci-Fi", "Horror" },
            new[] { "Sci-Fi", "Action" }
        };

        var result = BoxSetMetadataProvider.ResolveCollectionStringList(
            aggregate: true,
            setNfoValues: ["Romance"],
            movieSources: movies,
            max: 0);

        Assert.Equal(3, result.Length);
        Assert.Equal("Sci-Fi", result[0]);
        Assert.DoesNotContain("Romance", result);
    }

    [Fact]
    public void ResolveCollectionStringList_AggregateOff_UsesSetNfo()
    {
        var result = BoxSetMetadataProvider.ResolveCollectionStringList(
            aggregate: false,
            setNfoValues: ["Action", "Sci-Fi"],
            movieSources: [new[] { "Horror" }],
            max: 0);

        Assert.Equal(["Action", "Sci-Fi"], result);
    }

    [Fact]
    public void ResolveCollectionStringList_AggregateOff_EmptySetNfo_Clears()
    {
        var result = BoxSetMetadataProvider.ResolveCollectionStringList(
            aggregate: false,
            setNfoValues: [],
            movieSources: [new[] { "Horror" }],
            max: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveCollectionStringList_AggregateOff_NullSetNfo_Clears()
    {
        var result = BoxSetMetadataProvider.ResolveCollectionStringList(
            aggregate: false,
            setNfoValues: null,
            movieSources: [new[] { "Horror" }],
            max: 0);

        Assert.Empty(result);
    }
}
