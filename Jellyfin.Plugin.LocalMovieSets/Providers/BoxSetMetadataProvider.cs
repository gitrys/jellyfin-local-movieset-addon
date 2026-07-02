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
    private readonly ILibraryManager _libraryManager;
    private readonly LocalMovieSetManager _setManager;
    private readonly SetNfoParser _setNfoParser;
    private readonly ILogger<BoxSetMetadataProvider> _logger;

    // Reflection lookups for people APIs whose signatures differ across
    // Jellyfin 10.x releases.
    private readonly System.Reflection.MethodInfo? _updatePeopleAsyncMethod;
    private readonly System.Reflection.MethodInfo? _updatePeopleMethod;
    private readonly System.Reflection.MethodInfo? _getPeopleMethod;

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

        _updatePeopleAsyncMethod = typeof(ILibraryManager).GetMethods()
            .FirstOrDefault(m => m.Name == "UpdatePeopleAsync" && m.GetParameters().Length == 3);

        _updatePeopleMethod = typeof(ILibraryManager).GetMethods()
            .FirstOrDefault(m => m.Name == "UpdatePeople" && m.GetParameters().Length == 2);

        _getPeopleMethod = typeof(ILibraryManager).GetMethods()
            .FirstOrDefault(m => m.Name == "GetPeople" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(BaseItem));
    }

    /// <inheritdoc />
    public string Name => "Local Movie Sets";

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

        var movies = item.GetLinkedChildren().OfType<Movie>().ToList();

        var changed = ApplyDisplayOrder(item, config);
        changed |= ApplySetMetadata(item, item.Name, movies, config);

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
        if (!string.IsNullOrWhiteSpace(config.SetDataFolder))
        {
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

                var genresArray = setInfo.Genres.ToArray();
                if (collection.Genres is null || !collection.Genres.SequenceEqual(genresArray, StringComparer.Ordinal))
                {
                    collection.Genres = genresArray;
                    changed = true;
                }

                var studiosArray = setInfo.Studios.ToArray();
                if (collection.Studios is null || !collection.Studios.SequenceEqual(studiosArray, StringComparer.Ordinal))
                {
                    collection.Studios = studiosArray;
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
            var uniqueTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var movie in movies)
            {
                if (movie.Tags != null)
                {
                    foreach (var tag in movie.Tags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            uniqueTags.Add(tag.Trim());
                        }
                    }
                }
            }

            var tagsArray = uniqueTags.ToArray();
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
    /// Aggregates directors, writers and top-billed actors from the member
    /// movies onto the collection. People are persisted through the library
    /// manager's UpdatePeople path; running inside the refresh keeps this
    /// serialized with the item save.
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

        var aggregatedPeople = new List<PersonInfo>();
        var seenPeople = new HashSet<(string Name, PersonKind Type)>();

        foreach (var movie in movies)
        {
            var moviePeople = GetPeopleSafe(movie);
            if (moviePeople == null) continue;

            int actorCount = 0;
            foreach (var person in moviePeople)
            {
                if (person == null || string.IsNullOrWhiteSpace(person.Name)) continue;

                var type = person.Type;
                var nameKey = person.Name.Trim();

                if (type == PersonKind.Director || type == PersonKind.Writer)
                {
                    var key = (nameKey.ToLowerInvariant(), type);
                    if (!seenPeople.Contains(key))
                    {
                        seenPeople.Add(key);
                        aggregatedPeople.Add(new PersonInfo
                        {
                            Name = nameKey,
                            Type = type,
                            Role = person.Role,
                            ImageUrl = person.ImageUrl,
                            ProviderIds = person.ProviderIds,
                            SortOrder = person.SortOrder
                        });
                    }
                }
                else if (type == PersonKind.Actor)
                {
                    if (actorCount < 10)
                    {
                        actorCount++;
                        var key = (nameKey.ToLowerInvariant(), type);
                        if (!seenPeople.Contains(key))
                        {
                            seenPeople.Add(key);
                            aggregatedPeople.Add(new PersonInfo
                            {
                                Name = nameKey,
                                Type = type,
                                Role = person.Role,
                                ImageUrl = person.ImageUrl,
                                ProviderIds = person.ProviderIds,
                                SortOrder = person.SortOrder
                            });
                        }
                    }
                }
            }
        }

        try
        {
            if (_updatePeopleAsyncMethod != null)
            {
                var task = (Task?)_updatePeopleAsyncMethod.Invoke(_libraryManager, new object[] { collection, aggregatedPeople, cancellationToken });
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            else if (_updatePeopleMethod != null)
            {
                _updatePeopleMethod.Invoke(_libraryManager, new object[] { collection, aggregatedPeople });
            }
            else
            {
                _logger.LogError("No UpdatePeople or UpdatePeopleAsync method found on ILibraryManager.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update aggregated people for collection '{SetName}'", setName);
        }
    }

    private IReadOnlyList<PersonInfo> GetPeopleSafe(BaseItem item)
    {
        if (_getPeopleMethod != null)
        {
            try
            {
                var result = _getPeopleMethod.Invoke(_libraryManager, new object[] { item });
                if (result is IReadOnlyList<PersonInfo> readOnlyList)
                {
                    return readOnlyList;
                }
                if (result is System.Collections.IEnumerable enumerable)
                {
                    return enumerable.Cast<PersonInfo>().ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking GetPeople via reflection for item '{ItemName}'", item.Name);
            }
        }
        else
        {
            _logger.LogError("GetPeople method not found on ILibraryManager via reflection.");
        }

        return Array.Empty<PersonInfo>();
    }
}
