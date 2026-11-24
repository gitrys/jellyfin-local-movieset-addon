using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Providers;

/// <summary>
/// Applies set metadata to BoxSet collections inside Jellyfin's metadata
/// refresh pipeline: title/overview/genres/studios/provider IDs from the
/// dedicated TMM set NFO, chronological premiere date, display order, and
/// aggregated ratings/tags/people from the member movies.
/// The pipeline persists the item once after all providers ran, so there are
/// no concurrent collection.xml writes.
/// </summary>
public class BoxSetMetadataProvider : ICustomMetadataProvider<BoxSet>
{
    /// <summary>Provider name. This provider does not appear in the metadata downloader list.</summary>
    public const string ProviderName = "Local Movie Sets";

    private readonly ILibraryManager _libraryManager;
    private readonly LocalMovieSetManager _setManager;
    private readonly SetNfoParser _setNfoParser;
    private readonly ILogger<BoxSetMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoxSetMetadataProvider"/> class.
    /// </summary>
    public BoxSetMetadataProvider(
        ILibraryManager libraryManager,
        LocalMovieSetManager setManager,
        SetNfoParser setNfoParser,
        ILogger<BoxSetMetadataProvider> logger)
    {
        _libraryManager = libraryManager;
        _setManager = setManager;
        _setNfoParser = setNfoParser;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<ItemUpdateType> FetchAsync(BoxSet item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return ItemUpdateType.None;
        }

        // Only touch collections this plugin manages. Unmanaged user
        // collections (created manually or by other plugins) are skipped.
        if (!CollectionOwnership.IsManaged(item, _setManager.IsManagedSet))
        {
            return ItemUpdateType.None;
        }

        // A collection whose name is a set of the current sync gets the
        // ownership marker. It is persisted by the refresh pipeline together
        // with the other changes; if the refresh fails, the collection stays
        // unmarked and is never deleted by the plugin.
        var newlyOwned = _setManager.IsManagedSet(item.Name)
            && CollectionOwnership.MarkOwned(item, item.Name);
        var changed = newlyOwned;

        var movies = _setManager.GetBoxSetMovies(item);

        changed |= ApplyDisplayOrder(item, config, newlyOwned);
        changed |= ApplySetMetadata(item, item.Name, movies, config);
        changed |= ApplyThemeSong(item, item.Name, config);

        await UpdatePeopleAsync(item, item.Name, movies, config, cancellationToken).ConfigureAwait(false);

        return changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }

    /// <summary>
    /// Applies the configured sort order only to collections that just became
    /// plugin-owned (new or first adopted) or have an empty display order (written
    /// by the removed "Creation Order" setting, or by the reset button in Jellyfin's
    /// metadata editor; Jellyfin sorts an empty value by release date). Any order
    /// chosen in the editor ("Default" shown as "Date modified", "SortName",
    /// "PremiereDate") is kept.
    /// </summary>
    private static bool ApplyDisplayOrder(BoxSet collection, PluginConfiguration config, bool newlyOwned)
    {
        if (!ShouldApplyDisplayOrder(newlyOwned, collection.DisplayOrder))
        {
            return false;
        }

        var mapped = MapSortByToJellyfin(config.CollectionSortBy);
        if (!string.Equals(collection.DisplayOrder, mapped, StringComparison.Ordinal))
        {
            collection.DisplayOrder = mapped;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the plugin may set a collection's display order.
    /// </summary>
    /// <param name="newlyOwned"><c>true</c> when the collection got the ownership marker in this refresh.</param>
    /// <param name="currentDisplayOrder">The collection's current display order.</param>
    /// <returns><c>true</c> for new collections and for an empty display order.</returns>
    internal static bool ShouldApplyDisplayOrder(bool newlyOwned, string? currentDisplayOrder)
    {
        return newlyOwned || string.IsNullOrWhiteSpace(currentDisplayOrder);
    }

    /// <summary>
    /// Maps the configured sort mode to a Jellyfin BoxSet DisplayOrder value.
    /// The plugin offers "SortName" and "PremiereDate"; everything else (including
    /// the removed "Default" setting) maps to "PremiereDate".
    /// </summary>
    /// <summary>
    /// Applies the set NFO's sort title. Compares against <see cref="BaseItem.ForcedSortName"/>,
    /// which holds the stored value; <see cref="BaseItem.SortName"/> returns Jellyfin's
    /// normalized form (lower case, padded numbers) and would never match the raw title.
    /// </summary>
    /// <returns><c>true</c> if the sort title was changed.</returns>
    internal static bool ApplySortTitle(BoxSet collection, string? sortTitle)
    {
        if (string.IsNullOrWhiteSpace(sortTitle)
            || string.Equals(collection.ForcedSortName, sortTitle, StringComparison.Ordinal))
        {
            return false;
        }

        collection.SortName = sortTitle;
        collection.ForcedSortName = sortTitle;
        return true;
    }

    internal static string MapSortByToJellyfin(string? sortBy)
    {
        return string.Equals(sortBy, "SortName", StringComparison.Ordinal)
            ? "SortName"
            : "PremiereDate";
    }

    /// <summary>
    /// Reads the dedicated set NFO (if available) and applies overview/title to
    /// the BoxSet in memory. Also calculates the collection's PremiereDate
    /// chronologically based on configuration and aggregates ratings/tags from
    /// the member movies. Persistence is handled by the refresh pipeline.
    /// </summary>
    /// <returns><c>true</c> if any property was modified.</returns>
    private bool ApplySetMetadata(
        BoxSet collection,
        string setName,
        List<Movie> movies,
        PluginConfiguration config)
    {
        var changed = false;

        // 1. Calculate collection PremiereDate based on oldest movie
        if (config.CollectionReleaseDate != CollectionReleaseDateMode.DoNotCalculate)
        {
            var oldestMovie = movies
                .Where(m => m.PremiereDate.GetValueOrDefault() > DateTime.MinValue)
                .OrderBy(m => m.PremiereDate.GetValueOrDefault())
                .FirstOrDefault();

            if (oldestMovie != null)
            {
                var targetDate = oldestMovie.PremiereDate;
                if (config.CollectionReleaseDate == CollectionReleaseDateMode.AlwaysOverwrite
                    || (config.CollectionReleaseDate == CollectionReleaseDateMode.SetOnlyIfEmpty && !collection.PremiereDate.HasValue))
                {
                    if (collection.PremiereDate != targetDate)
                    {
                        collection.PremiereDate = targetDate;
                        changed = true;
                    }

                    if (targetDate.HasValue)
                    {
                        var targetYear = targetDate.Value.Year;
                        if (collection.ProductionYear != targetYear)
                        {
                            collection.ProductionYear = targetYear;
                            changed = true;
                        }
                    }
                }
            }
        }

        // 2. Read the dedicated set NFO. Only a successfully read NFO may change
        // set-derived fields; an unreachable folder, a missing NFO, or an
        // unreadable NFO leave the collection's current values alone.
        var setInfo = _setNfoParser.ParseSet(config.SetDataFolder, setName, config.NfoNaming);
        if (setInfo != null)
        {
            if (!string.IsNullOrWhiteSpace(setInfo.Overview)
                && !string.Equals(collection.Overview, setInfo.Overview, StringComparison.Ordinal))
            {
                collection.Overview = setInfo.Overview;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(setInfo.OriginalTitle)
                && !string.Equals(collection.OriginalTitle, setInfo.OriginalTitle, StringComparison.Ordinal))
            {
                collection.OriginalTitle = setInfo.OriginalTitle;
                changed = true;
            }

            if (ApplySortTitle(collection, setInfo.SortTitle))
            {
                changed = true;
            }

            if (!string.IsNullOrEmpty(setInfo.TmdbId)
                && !string.Equals(collection.GetProviderId(MetadataProvider.Tmdb), setInfo.TmdbId, StringComparison.Ordinal))
            {
                collection.SetProviderId(MetadataProvider.Tmdb, setInfo.TmdbId);
                changed = true;
            }

            if (!string.IsNullOrEmpty(setInfo.ImdbId)
                && !string.Equals(collection.GetProviderId(MetadataProvider.Imdb), setInfo.ImdbId, StringComparison.Ordinal))
            {
                collection.SetProviderId(MetadataProvider.Imdb, setInfo.ImdbId);
                changed = true;
            }
        }

        // Genres: aggregation from movies wins when enabled; otherwise the set NFO.
        // Without a readable set NFO (null) the current genres are kept.
        var genresArray = ResolveCollectionStringList(
            config.AggregateGenres,
            setInfo?.Genres,
            movies.Select(m => m.Genres),
            config.MaxGenres);
        if (genresArray is not null
            && (collection.Genres is null || !collection.Genres.SequenceEqual(genresArray, StringComparer.OrdinalIgnoreCase)))
        {
            collection.Genres = genresArray;
            changed = true;
        }

        // Studios: same rule as genres.
        var studiosArray = ResolveCollectionStringList(
            config.AggregateStudios,
            setInfo?.Studios,
            movies.Select(m => m.Studios),
            config.MaxStudios);
        if (studiosArray is not null
            && (collection.Studios is null || !collection.Studios.SequenceEqual(studiosArray, StringComparer.OrdinalIgnoreCase)))
        {
            collection.Studios = studiosArray;
            changed = true;
        }

        // 3. Aggregate community rating and tags
        if (config.AggregateRatings)
        {
            var ratings = movies
                .Where(m => m.CommunityRating.HasValue)
                .Select(m => m.CommunityRating!.Value)
                .ToList();

            float? avgRating = null;
            if (ratings.Count > 0)
            {
                avgRating = (float)Math.Round(ratings.Average(), 1);
            }

            if (collection.CommunityRating != avgRating)
            {
                collection.CommunityRating = avgRating;
                changed = true;
            }
        }

        if (config.AggregateTags)
        {
            var tagsArray = RankStringsByFrequency(movies.Select(m => m.Tags), config.MaxTags).ToArray();
            var currentTags = collection.Tags ?? Array.Empty<string>();
            if (!currentTags.SequenceEqual(tagsArray, StringComparer.OrdinalIgnoreCase))
            {
                collection.Tags = tagsArray;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Synchronizes theme music (e.g. theme.mp3, theme.flac) from the set data folder
    /// into the Jellyfin BoxSet directory, enabling native theme music playback.
    /// </summary>
    private bool ApplyThemeSong(BoxSet collection, string setName, PluginConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(collection.Path) || !Directory.Exists(collection.Path))
        {
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(config.SetDataFolder))
            {
                return false;
            }

            var sourceThemeSong = SetNfoParser.ResolveThemeSongPath(config.SetDataFolder, setName, config.NfoNaming);
            if (sourceThemeSong is not null && File.Exists(sourceThemeSong))
            {
                var ext = Path.GetExtension(sourceThemeSong);
                var targetPath = Path.Combine(collection.Path, $"theme{ext}");

                // A copy with another extension is an older version of this theme
                // (the set folder is the source). The collection folder belongs to
                // Jellyfin and never holds media, so removing it is safe.
                var removedOld = RemoveOtherThemeCopies(collection.Path, ext, setName);

                var needCopy = true;
                if (File.Exists(targetPath))
                {
                    var sourceInfo = new FileInfo(sourceThemeSong);
                    var targetInfo = new FileInfo(targetPath);
                    if (sourceInfo.Length == targetInfo.Length
                        && sourceInfo.LastWriteTimeUtc == targetInfo.LastWriteTimeUtc)
                    {
                        needCopy = false;
                    }
                }

                if (needCopy)
                {
                    File.Copy(sourceThemeSong, targetPath, overwrite: true);
                    File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourceThemeSong));
                    _logger.LogInformation(
                        "Linked theme song '{SourceFile}' to collection folder for '{SetName}'",
                        Path.GetFileName(sourceThemeSong),
                        setName);
                    return true;
                }

                return removedOld;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply theme song for collection '{SetName}'", setName);
        }

        return false;
    }

    /// <summary>
    /// Deletes <c>theme.*</c> copies with a supported extension other than <paramref name="keepExtension"/>.
    /// </summary>
    /// <returns><c>true</c> if a file was deleted.</returns>
    private bool RemoveOtherThemeCopies(string collectionFolder, string keepExtension, string setName)
    {
        var removed = false;
        foreach (var extension in SetNfoParser.ThemeSongExtensions)
        {
            if (string.Equals(extension, keepExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var oldCopy = Path.Combine(collectionFolder, "theme" + extension);
            if (File.Exists(oldCopy))
            {
                File.Delete(oldCopy);
                removed = true;
                _logger.LogInformation(
                    "Removed old theme copy '{File}' from collection folder for '{SetName}'",
                    Path.GetFileName(oldCopy),
                    setName);
            }
        }

        return removed;
    }

    /// <summary>
    /// Resolves genres or studios for a collection.
    /// When <paramref name="aggregate"/> is true, values come from member movies.
    /// Otherwise values come from the set NFO: a read NFO without values clears the field,
    /// and no readable NFO (<paramref name="setNfoValues"/> is <c>null</c>) returns <c>null</c>,
    /// meaning the current values are kept.
    /// </summary>
    /// <param name="aggregate">Whether to aggregate from member movies.</param>
    /// <param name="setNfoValues">Values from a successfully read set NFO, or <c>null</c> when there is none.</param>
    /// <param name="movieSources">Values per member movie.</param>
    /// <param name="max">Maximum number of values (0 = all).</param>
    /// <returns>The new values, or <c>null</c> to leave the field unchanged.</returns>
    public static string[]? ResolveCollectionStringList(
        bool aggregate,
        IReadOnlyList<string>? setNfoValues,
        IEnumerable<IEnumerable<string>?> movieSources,
        int max)
    {
        if (aggregate)
        {
            return RankStringsByFrequency(movieSources, max).ToArray();
        }

        if (setNfoValues is null)
        {
            return null;
        }

        return max > 0
            ? setNfoValues.Take(max).ToArray()
            : setNfoValues.ToArray();
    }

    /// <summary>
    /// Counts occurrences of non-empty strings across input collections (each collection adds at most 1 count),
    /// sorts descending by frequency (and stable ascending by string), and limits the output if <paramref name="limit"/> > 0.
    /// </summary>
    public static IReadOnlyList<string> RankStringsByFrequency(IEnumerable<IEnumerable<string>?> sources, int limit)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sources != null)
        {
            foreach (var source in sources)
            {
                if (source == null) continue;
                var seenInSource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in source)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        var clean = item.Trim();
                        if (seenInSource.Add(clean))
                        {
                            counts[clean] = counts.TryGetValue(clean, out var c) ? c + 1 : 1;
                        }
                    }
                }
            }
        }

        var sorted = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key);

        return limit > 0 ? sorted.Take(limit).ToArray() : sorted.ToArray();
    }

    /// <summary>
    /// Ranks people of a specific <see cref="PersonKind"/> across multiple movie sources by frequency of appearance.
    /// </summary>
    public static IReadOnlyList<PersonInfo> RankPeopleByFrequency(
        IEnumerable<IEnumerable<PersonInfo>?> sources,
        PersonKind kind,
        int maxPerSource,
        int limit)
    {
        var map = new Dictionary<string, PersonCandidate>(StringComparer.OrdinalIgnoreCase);
        if (sources != null)
        {
            foreach (var source in sources)
            {
                if (source == null) continue;
                var seenInSource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int countInSource = 0;
                foreach (var p in source)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                    if (p.Type != kind) continue;

                    var name = p.Name.Trim();
                    if ((maxPerSource <= 0 || countInSource < maxPerSource) && seenInSource.Add(name))
                    {
                        countInSource++;
                        if (!map.TryGetValue(name, out var cand))
                        {
                            cand = new PersonCandidate { Info = ClonePersonInfo(p, name) };
                            map[name] = cand;
                        }
                        cand.MovieCount++;
                    }
                }
            }
        }

        var sorted = map.Values
            .OrderByDescending(c => c.MovieCount)
            .ThenBy(c => c.Info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Info);

        return limit > 0 ? sorted.Take(limit).ToArray() : sorted.ToArray();
    }

    /// <summary>
    /// Aggregates directors, writers and actors from the member movies onto the
    /// collection, ranked by frequency of appearance across the set.
    /// People are persisted through the library manager's UpdatePeople path.
    /// </summary>
    private async Task UpdatePeopleAsync(
        BoxSet collection,
        string setName,
        List<Movie> movies,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.AggregatePeople)
        {
            return;
        }

        var moviePeopleSources = movies.Select(GetPeopleSafe).ToList();

        var rankedDirectors = RankPeopleByFrequency(moviePeopleSources, PersonKind.Director, 0, config.MaxDirectors);
        var rankedWriters = RankPeopleByFrequency(moviePeopleSources, PersonKind.Writer, 0, config.MaxWriters);
        var rankedActors = RankPeopleByFrequency(moviePeopleSources, PersonKind.Actor, 0, config.MaxActors);

        var aggregatedPeople = rankedDirectors
            .Concat(rankedWriters)
            .Concat(rankedActors)
            .ToList();

        try
        {
            await _libraryManager.UpdatePeopleAsync(collection, aggregatedPeople, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update aggregated people for collection '{SetName}'", setName);
        }
    }

    private static PersonInfo ClonePersonInfo(PersonInfo person, string cleanName) => new()
    {
        Name = cleanName,
        Type = person.Type,
        Role = person.Role,
        ImageUrl = person.ImageUrl,
        ProviderIds = person.ProviderIds,
        SortOrder = person.SortOrder
    };

    private sealed class PersonCandidate
    {
        public required PersonInfo Info { get; init; }
        public int MovieCount { get; set; }
    }

    private IReadOnlyList<PersonInfo> GetPeopleSafe(BaseItem item)
    {
        try
        {
            return _libraryManager.GetPeople(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting people for item '{ItemName}'", item.Name);
            return Array.Empty<PersonInfo>();
        }
    }
}
