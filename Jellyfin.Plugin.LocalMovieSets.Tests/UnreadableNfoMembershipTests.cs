using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// A movie whose NFO cannot be read (for example while a media manager writes it)
/// stays in the managed collection it currently belongs to.
/// </summary>
public class UnreadableNfoMembershipTests
{
    private static Movie NewMovie(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static Dictionary<string, List<Movie>> Groups() =>
        new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void UnreadableMember_IsKeptInItsCurrentSet()
    {
        var alien = NewMovie("Alien");
        var aliens = NewMovie("Aliens");
        var groups = Groups();
        groups["Alien Collection"] = [alien];

        var kept = LocalMovieSetManager.MergeUnreadableMembers(
            groups,
            [("Alien Collection", new List<Movie> { alien, aliens })],
            new HashSet<Guid> { aliens.Id });

        Assert.Equal([aliens], kept);
        Assert.Equal([alien, aliens], groups["Alien Collection"]);
    }

    [Fact]
    public void CollectionWithOnlyUnreadableMembers_IsNotOrphaned()
    {
        var alien = NewMovie("Alien");
        var groups = Groups();

        LocalMovieSetManager.MergeUnreadableMembers(
            groups,
            [("alien collection", new List<Movie> { alien })],
            new HashSet<Guid> { alien.Id });

        Assert.True(groups.ContainsKey("Alien Collection"));
        Assert.Equal([alien], groups["Alien Collection"]);
    }

    [Fact]
    public void ReadableMemberRemovedFromNfo_IsNotKept()
    {
        var alien = NewMovie("Alien");
        var other = NewMovie("Other");
        var groups = Groups();
        groups["Alien Collection"] = [alien];

        var kept = LocalMovieSetManager.MergeUnreadableMembers(
            groups,
            [("Alien Collection", new List<Movie> { alien, other })],
            new HashSet<Guid>());

        Assert.Empty(kept);
        Assert.Equal([alien], groups["Alien Collection"]);
    }

    [Fact]
    public void UnreadableMovieAlreadyInGroup_IsNotAddedTwice()
    {
        var alien = NewMovie("Alien");
        var groups = Groups();
        groups["Alien Collection"] = [alien];

        var kept = LocalMovieSetManager.MergeUnreadableMembers(
            groups,
            [("Alien Collection", new List<Movie> { alien })],
            new HashSet<Guid> { alien.Id });

        Assert.Empty(kept);
        Assert.Single(groups["Alien Collection"]);
    }

    [Fact]
    public void UnreadableMovieNotInAnyCollection_IsNotGrouped()
    {
        var alien = NewMovie("Alien");
        var groups = Groups();

        var kept = LocalMovieSetManager.MergeUnreadableMembers(
            groups,
            [("Predator Collection", new List<Movie>())],
            new HashSet<Guid> { alien.Id });

        Assert.Empty(kept);
        Assert.Empty(groups);
    }
}
