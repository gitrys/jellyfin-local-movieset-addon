using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Result of <see cref="OrphanPlanner.Plan{T}"/>.
/// </summary>
/// <typeparam name="T">Collection type.</typeparam>
public sealed class OrphanPlan<T>
{
    /// <summary>Gets the owned collections whose set no longer appears in any movie NFO.</summary>
    public List<T> ToDelete { get; } = [];

    /// <summary>Gets the collections without a set in any movie NFO that this plugin does not own. They are never deleted.</summary>
    public List<T> Unowned { get; } = [];

    /// <summary>Gets or sets the number of owned collections, used as the base for the limit.</summary>
    public int OwnedCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the safety limit blocks the deletion of <see cref="ToDelete"/>.</summary>
    public bool LimitBlocked { get; set; }
}

/// <summary>
/// Decides which collections an orphan cleanup may delete. Shared by the sync
/// and the preview so both always reach the same result.
/// </summary>
public static class OrphanPlanner
{
    /// <summary>
    /// Below this number of deletions the percentage limit never applies,
    /// so small libraries can still clean up.
    /// </summary>
    public const int MinimumDeletionsForLimit = 5;

    /// <summary>
    /// Plans an orphan cleanup.
    /// </summary>
    /// <typeparam name="T">Collection type.</typeparam>
    /// <param name="collections">All collections in Jellyfin.</param>
    /// <param name="getName">Returns a collection's name.</param>
    /// <param name="isOwned">Returns whether this plugin owns a collection.</param>
    /// <param name="activeSetNames">Set names found in movie NFOs (compared case-insensitively).</param>
    /// <param name="maxPercent">
    /// Largest share of owned collections one cleanup may delete. <c>0</c> or less, or <c>100</c> and more, means no limit.
    /// </param>
    /// <returns>The plan.</returns>
    public static OrphanPlan<T> Plan<T>(
        IEnumerable<T> collections,
        Func<T, string> getName,
        Func<T, bool> isOwned,
        IEnumerable<string> activeSetNames,
        int maxPercent)
    {
        var active = new HashSet<string>(activeSetNames, StringComparer.OrdinalIgnoreCase);
        var plan = new OrphanPlan<T>();

        foreach (var collection in collections)
        {
            var owned = isOwned(collection);
            if (owned)
            {
                plan.OwnedCount++;
            }

            if (active.Contains(getName(collection) ?? string.Empty))
            {
                continue;
            }

            if (owned)
            {
                plan.ToDelete.Add(collection);
            }
            else
            {
                plan.Unowned.Add(collection);
            }
        }

        plan.LimitBlocked = IsOverLimit(plan.ToDelete.Count, plan.OwnedCount, maxPercent);
        return plan;
    }

    /// <summary>
    /// Returns whether deleting <paramref name="deleteCount"/> of <paramref name="ownedCount"/>
    /// owned collections exceeds the limit.
    /// </summary>
    /// <param name="deleteCount">Collections that would be deleted.</param>
    /// <param name="ownedCount">Owned collections in total.</param>
    /// <param name="maxPercent">Limit in percent; see <see cref="Plan{T}"/>.</param>
    /// <returns><c>true</c> when the deletion must not run.</returns>
    public static bool IsOverLimit(int deleteCount, int ownedCount, int maxPercent)
    {
        if (maxPercent <= 0 || maxPercent >= 100)
        {
            return false;
        }

        if (deleteCount < MinimumDeletionsForLimit)
        {
            return false;
        }

        return (long)deleteCount * 100 > (long)maxPercent * ownedCount;
    }
}
