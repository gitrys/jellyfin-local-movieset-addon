using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class OrphanPlannerTests
{
    private sealed record Collection(string Name, bool Owned);

    private static OrphanPlan<Collection> Plan(IEnumerable<Collection> collections, IEnumerable<string> activeSets, int maxPercent = 25)
        => OrphanPlanner.Plan(collections, c => c.Name, c => c.Owned, activeSets, maxPercent);

    private static List<Collection> Owned(string prefix, int count)
        => Enumerable.Range(1, count).Select(i => new Collection($"{prefix} {i}", true)).ToList();

    [Fact]
    public void UnownedOrphan_IsNeverDeleted()
    {
        var plan = Plan([new Collection("Christmas Movies", false)], ["Alien Collection"]);

        Assert.Empty(plan.ToDelete);
        Assert.Single(plan.Unowned);
    }

    [Fact]
    public void OwnedOrphan_IsDeleted()
    {
        var plan = Plan(
            [new Collection("Alien Collection", true), new Collection("Old Set", true)],
            ["Alien Collection"]);

        Assert.Equal("Old Set", Assert.Single(plan.ToDelete).Name);
        Assert.Equal(2, plan.OwnedCount);
        Assert.False(plan.LimitBlocked);
    }

    [Fact]
    public void ActiveSetNames_AreComparedCaseInsensitively()
    {
        var plan = Plan([new Collection("Alien Collection", true)], ["ALIEN collection"]);

        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void Limit_BlocksWhenShareIsExceeded()
    {
        // 6 of 20 = 30 % > 25 %
        var collections = Owned("Active", 14).Concat(Owned("Gone", 6));
        var active = Enumerable.Range(1, 14).Select(i => $"Active {i}");

        var plan = Plan(collections, active);

        Assert.True(plan.LimitBlocked);
        Assert.Equal(6, plan.ToDelete.Count);
    }

    [Fact]
    public void Limit_AllowsExactlyTheLimit()
    {
        // 5 of 20 = 25 %, not more than 25 %
        var collections = Owned("Active", 15).Concat(Owned("Gone", 5));
        var active = Enumerable.Range(1, 15).Select(i => $"Active {i}");

        Assert.False(Plan(collections, active).LimitBlocked);
    }

    [Fact]
    public void Limit_IgnoredBelowMinimumDeletions()
    {
        // 4 of 4 = 100 %, but fewer than 5 deletions
        Assert.False(Plan(Owned("Gone", 4), []).LimitBlocked);
    }

    [Fact]
    public void Limit_ZeroMeansNoLimit()
    {
        Assert.False(Plan(Owned("Gone", 50), [], maxPercent: 0).LimitBlocked);
    }

    [Theory]
    [InlineData(6, 20, 25, true)]
    [InlineData(5, 20, 25, false)]
    [InlineData(4, 4, 25, false)]
    [InlineData(10, 10, 100, false)]
    [InlineData(10, 10, 0, false)]
    [InlineData(10, 10, -5, false)]
    public void IsOverLimit_Cases(int delete, int owned, int maxPercent, bool expected)
    {
        Assert.Equal(expected, OrphanPlanner.IsOverLimit(delete, owned, maxPercent));
    }
}
