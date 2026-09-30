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
        if (!IsManagedCollection(item.Name, config))
        {
            return ItemUpdateType.None;
        }

        var movies = _setManager.GetBoxSetMovies(item);

        var changed = ApplyDisplayOrder(item, config);
        changed |= ApplySetMetadata(item, item.Name, movies, config);
        changed |= ApplyThemeSong(item, item.Name, movies, config);

        await UpdatePeopleAsync(item, item.Name, movies, config, cancellationToken).ConfigureAwait(false);

        return changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }

    /// <summary>
    /// A collection counts as managed when the manager saw its name as a set
    /// group in the last sync, or (after a server restart, before the first
    /// sync) when a dedicated set NFO or artwork folder exists for the name.
    /// </summary>
    private bool IsManagedCollection(string collectionName, PluginConfiguration config)
    {
        if (_setManager.IsManagedSet(collectionName))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(config.SetDataFolder))
        {
            return false;
        }

        if (SetNfoParser.ResolveNfoPath(config.SetDataFolder, collectionName, config.NfoNaming) is not null)
        {
            return true;
        }

        return SetNfoParser.ResolveArtworkFolder(config.SetDataFolder, collectionName, config.NfoNaming) is not null;
    }

    private static bool ApplyDisplayOrder(BoxSet collection, PluginConfiguration config)
    {
        var mapped = MapSortByToJellyfin(config.CollectionSortBy);
        if (!string.Equals(collection.DisplayOrder, mapped, StringComparison.Ordinal))
        {
            collection.DisplayOrder = mapped;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Maps the configured sort mode to a Jellyfin BoxSet DisplayOrder value.
    /// Jellyfin only honors "SortName" and "PremiereDate"; anything else
    /// (including legacy config values) means default linked-children order.
    /// </summary>
    internal static string MapSortByToJellyfin(string sortBy)
    {
        return sortBy switch
        {
            "SortName" => "SortName",
            "PremiereDate" => "PremiereDate",
            _ => string.Empty
        };
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

        // 2. Parse dedicated set NFO metadata if folder is configured
        SetNfoInfo? setInfo = null;
        if (!string.IsNullOrWhiteSpace(config.SetDataFolder))
        {
            setInfo = _setNfoParser.ParseSet(config.SetDataFolder, setName, config.NfoNaming);
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

                if (!string.IsNullOrWhiteSpace(setInfo.SortTitle)
                    && !string.Equals(collection.SortName, setInfo.SortTitle, StringComparison.Ordinal))
                {
                    collection.SortName = setInfo.SortTitle;
                    collection.ForcedSortName = setInfo.SortTitle;
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
        }

        // Genres: Set NFO takes priority if present, otherwise inherit from member movies if AggregateGenres is enabled
        IReadOnlyList<string> candidateGenres = Array.Empty<string>();
        if (setInfo?.Genres != null && setInfo.Genres.Count > 0)
        {
            candidateGenres = config.MaxGenres > 0
                ? setInfo.Genres.Take(config.MaxGenres).ToArray()
                : setInfo.Genres.ToArray();
        }
        else if (config.AggregateGenres)
        {
            candidateGenres = RankStringsByFrequency(movies.Select(m => m.Genres), config.MaxGenres);
        }

        if (candidateGenres.Count > 0)
        {
            var genresArray = candidateGenres.ToArray();
            if (collection.Genres is null || !collection.Genres.SequenceEqual(genresArray, StringComparer.OrdinalIgnoreCase))
            {
                collection.Genres = genresArray;
                changed = true;
            }
        }

        // Studios: Set NFO takes priority if present, otherwise inherit from member movies if AggregateStudios is enabled
        IReadOnlyList<string> candidateStudios = Array.Empty<string>();
        if (setInfo?.Studios != null && setInfo.Studios.Count > 0)
        {
            candidateStudios = config.MaxStudios > 0
                ? setInfo.Studios.Take(config.MaxStudios).ToArray()
                : setInfo.Studios.ToArray();
        }
        else if (config.AggregateStudios)
        {
            candidateStudios = RankStringsByFrequency(movies.Select(m => m.Studios), config.MaxStudios);
        }

        if (candidateStudios.Count > 0)
        {
            var studiosArray = candidateStudios.ToArray();
            if (collection.Studios is null || !collection.Studios.SequenceEqual(studiosArray, StringComparer.OrdinalIgnoreCase))
            {
                collection.Studios = studiosArray;
                changed = true;
            }
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
    /// (or member movie folder fallback) into the Jellyfin BoxSet directory, enabling native theme music playback.
    /// </summary>
    private bool ApplyThemeSong(BoxSet collection, string setName, List<Movie> movies, PluginConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(collection.Path) || !Directory.Exists(collection.Path))
        {
            return false;
        }

        try
        {
            string? sourceThemeSong = null;
            if (!string.IsNullOrWhiteSpace(config.SetDataFolder))
            {
                sourceThemeSong = SetNfoParser.ResolveThemeSongPath(config.SetDataFolder, setName, config.NfoNaming);
            }

            if (sourceThemeSong is null && config.EnableMovieFolderArtworkFallback)
            {
                sourceThemeSong = SetNfoParser.ResolveMovieFolderThemeSong(movies.Select(m => m.ContainingFolderPath));
            }

            if (sourceThemeSong is not null && File.Exists(sourceThemeSong))
            {
                var ext = Path.GetExtension(sourceThemeSong);
                var targetPath = Path.Combine(collection.Path, $"theme{ext}");

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
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply theme song for collection '{SetName}'", setName);
        }

        return false;
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
