using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Core plugin service that:
/// <list type="bullet">
///   <item>Subscribes to Jellyfin library events (via <see cref="IHostedService"/>)</item>
///   <item>Scans movie NFO files for <c>&lt;set&gt;</c> tags</item>
///   <item>Creates or updates Jellyfin BoxSet collections via <see cref="ICollectionManager"/></item>
///   <item>Applies artwork and metadata from the TMM set data folder</item>
/// </list>
/// </summary>
public class LocalMovieSetManager : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly SetArtworkProvider _artworkProvider;
    private readonly MovieNfoParser _movieNfoParser;
    private readonly SetNfoParser _setNfoParser;
    private readonly ILogger<LocalMovieSetManager> _logger;

    // Debounce timer: fires 30 seconds after the last library change event
    private readonly Timer _debounceTimer;

    // Prevents concurrent syncs (debounce timer vs scheduled task)
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetManager"/> class.
    /// </summary>
    public LocalMovieSetManager(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        SetArtworkProvider artworkProvider,
        MovieNfoParser movieNfoParser,
        SetNfoParser setNfoParser,
        ILogger<LocalMovieSetManager> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _artworkProvider = artworkProvider;
        _movieNfoParser = movieNfoParser;
        _setNfoParser = setNfoParser;
        _logger = logger;

        // Timer starts stopped (Timeout.Infinite = never fire)
        _debounceTimer = new Timer(
            _ => _ = ScanAndSyncAsync(CancellationToken.None),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _logger.LogInformation("Local Movie Sets: library event listeners registered");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a library item is added or updated.
    /// Resets the debounce timer so a sync fires 30 seconds after the last event.
    /// </summary>
    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie)
        {
            _debounceTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Main sync entry point. Scans all movies, groups them by set name from their
    /// NFO files, then creates/updates Jellyfin BoxSet collections to match.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ScanAndSyncAsync(CancellationToken cancellationToken)
    {
        // Prevent overlapping syncs (debounce timer vs scheduled task)
        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Local Movie Sets: sync already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Local Movie Sets: starting sync");

            var config = Plugin.Instance?.Configuration;
            if (config is null)
            {
                _logger.LogError("Plugin configuration is not available");
                return;
            }

            // ── Step 1: Query all movies ──────────────────────────────────────
            var allMovies = _libraryManager
                .GetItemsResult(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    IsVirtualItem = false
                })
                .Items
                .OfType<Movie>()
                .ToList();

            _logger.LogInformation("Local Movie Sets: scanning {Count} movies", allMovies.Count);

            // ── Step 2: Parse NFOs — group movies by set name ─────────────────
            var setGroups = new Dictionary<string, List<Movie>>(StringComparer.OrdinalIgnoreCase);

            foreach (var movie in allMovies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(movie.Path))
                    continue;

                var membership = _movieNfoParser.ParseNfo(movie.Path);
                if (membership is null)
                    continue;

                if (!setGroups.TryGetValue(membership.SetName, out var group))
                {
                    group = [];
                    setGroups[membership.SetName] = group;
                }

                group.Add(movie);
            }

            _logger.LogInformation(
                "Local Movie Sets: found {SetCount} distinct sets across {MovieCount} movies",
                setGroups.Count,
                setGroups.Values.Sum(g => g.Count));

            // ── Step 3: Load existing BoxSet collections ───────────────────────
            var existingBoxSets = _libraryManager
                .GetItemsResult(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.BoxSet]
                })
                .Items
                .OfType<BoxSet>()
                .ToList();

            var existingByName = existingBoxSets
                .ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

            // ── Step 4: Create or update collections ───────────────────────────
            foreach (var (setName, movies) in setGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var minimumMovies = Math.Max(1, config.MinimumMovies);
                if (movies.Count < minimumMovies)
                {
                    _logger.LogDebug(
                        "Skipping set '{SetName}': {Count} movies < minimum {Min}",
                        setName, movies.Count, config.MinimumMovies);
                    continue;
                }

                if (existingByName.TryGetValue(setName, out var existingBoxSet))
                {
                    await UpdateExistingCollectionAsync(existingBoxSet, movies, setName, config, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await CreateNewCollectionAsync(setName, movies, config, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // ── Step 5: Optionally remove orphaned collections ─────────────────
            if (config.DeleteOrphanedSets)
            {
                await DeleteOrphanedSetsAsync(existingBoxSets, setGroups, config.DeleteSetsWithProviderId, cancellationToken)
                    .ConfigureAwait(false);
            }

            // ── Step 6: Cleanup legacy sort titles ──────────────────────────────
            await CleanupLegacySortTitlesAsync(allMovies, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Local Movie Sets: sync completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Local Movie Sets: sync was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local Movie Sets: sync failed with an unexpected error");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task CreateNewCollectionAsync(
        string setName,
        List<Movie> movies,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating new collection '{SetName}' with {Count} movie(s)",
            setName, movies.Count);

        try
        {
            var sortedMovies = SortMovies(movies, config.CollectionSortBy, config.CollectionSortOrder);

            var collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = setName,
                ItemIdList = sortedMovies.Select(m => m.Id.ToString()).ToArray()
            }).ConfigureAwait(false);

            if (collection is null)
            {
                _logger.LogWarning("CreateCollectionAsync returned null for '{SetName}'", setName);
                return;
            }

            collection.DisplayOrder = MapSortByToJellyfin(config.CollectionSortBy);
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);

            await ApplySetMetadataAsync(collection, setName, sortedMovies, config, cancellationToken)
                .ConfigureAwait(false);

            await _artworkProvider.SyncArtworkAsync(
                collection,
                config.SetDataFolder,
                setName,
                config.NfoNaming,
                overwrite: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection '{SetName}'", setName);
        }
    }

    private async Task UpdateExistingCollectionAsync(
        BoxSet boxSet,
        List<Movie> movies,
        string setName,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating existing collection '{SetName}' with {Count} movie(s)",
            setName, movies.Count);

        try
        {
            var sortedMovies = SortMovies(movies, config.CollectionSortBy, config.CollectionSortOrder);

            var existingMovieItems = boxSet
                .GetLinkedChildren()
                .OfType<Movie>()
                .ToList();

            var existingMovieIds = existingMovieItems
                .Select(c => c.Id)
                .ToHashSet();

            var targetMovieIds = sortedMovies
                .Select(m => m.Id)
                .ToHashSet();

            var newMovieIds = sortedMovies
                .Where(m => !existingMovieIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToArray();

            var movieIdsToRemove = existingMovieIds
                .Where(id => !targetMovieIds.Contains(id))
                .ToArray();

            if (newMovieIds.Length > 0)
            {
                _logger.LogInformation(
                    "Adding {Count} new movie(s) to collection '{SetName}'",
                    newMovieIds.Length, setName);

                await _collectionManager
                    .AddToCollectionAsync(boxSet.Id, newMovieIds)
                    .ConfigureAwait(false);
            }

            if (movieIdsToRemove.Length > 0)
            {
                _logger.LogInformation(
                    "Removing {Count} orphaned movie(s) from collection '{SetName}'",
                    movieIdsToRemove.Length, setName);

                await _collectionManager
                    .RemoveFromCollectionAsync(boxSet.Id, movieIdsToRemove)
                    .ConfigureAwait(false);
            }

            var changed = false;
            var mappedSortBy = MapSortByToJellyfin(config.CollectionSortBy);
            if (!string.Equals(boxSet.DisplayOrder, mappedSortBy, StringComparison.Ordinal))
            {
                boxSet.DisplayOrder = mappedSortBy;
                changed = true;
            }

            if (changed)
            {
                await boxSet.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                    .ConfigureAwait(false);
            }

            await ApplySetMetadataAsync(boxSet, setName, sortedMovies, config, cancellationToken)
                .ConfigureAwait(false);

            // Refresh artwork if requested
            if (config.UpdateExistingArtwork)
            {
                await _artworkProvider.SyncArtworkAsync(
                    boxSet,
                    config.SetDataFolder,
                    setName,
                    config.NfoNaming,
                    overwrite: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update collection '{SetName}'", setName);
        }
    }

    private List<Movie> SortMovies(List<Movie> movies, string sortBy, string sortOrder)
    {
        var isDescending = string.Equals(sortOrder, "Descending", StringComparison.OrdinalIgnoreCase);

        Func<Movie, object?> keySelector = sortBy switch
        {
            "SortName" => m => m.SortName,
            "Random" => m => Guid.NewGuid(),
            "CommunityRating" => m => m.CommunityRating,
            "CriticsRating" or "CriticRating" => m => m.CriticRating,
            "DateCreated" => m => m.DateCreated,
            "PremiereDate" => m => m.PremiereDate,
            "Runtime" => m => m.RunTimeTicks,
            _ => m => m.Name
        };

        if (string.Equals(sortBy, "Random", StringComparison.OrdinalIgnoreCase))
        {
            var random = new Random();
            return movies.OrderBy(_ => random.Next()).ToList();
        }

        if (isDescending)
        {
            return movies.OrderByDescending(keySelector).ThenByDescending(m => m.SortName).ToList();
        }
        else
        {
            return movies.OrderBy(keySelector).ThenBy(m => m.SortName).ToList();
        }
    }

    private static string MapSortByToJellyfin(string sortBy)
    {
        return sortBy switch
        {
            "CriticsRating" => "CriticRating",
            "ParentalRating" => "OfficialRating",
            _ => sortBy
        };
    }

    /// <summary>
    /// Reads the dedicated set NFO (if available) and applies overview/title to the BoxSet.
    /// Also calculates the collection's PremiereDate chronologically based on configuration.
    /// </summary>
    private async Task ApplySetMetadataAsync(
        BoxSet collection,
        string setName,
        List<Movie> movies,
        PluginConfiguration config,
        CancellationToken cancellationToken)
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

        if (changed)
        {
            await collection
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Updated metadata for collection '{SetName}'", setName);
        }
    }

    /// <summary>
    /// Deletes BoxSet collections that have no corresponding active set group.
    /// By default, only removes collections without a TMDB/IMDb provider ID.
    /// When <paramref name="includeWithProviderId"/> is true, all orphaned collections are deleted.
    /// </summary>
    private Task DeleteOrphanedSetsAsync(
        List<BoxSet> existingBoxSets,
        Dictionary<string, List<Movie>> activeSetGroups,
        bool includeWithProviderId,
        CancellationToken cancellationToken)
    {
        foreach (var boxSet in existingBoxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (activeSetGroups.ContainsKey(boxSet.Name))
                continue;

            // Safety guard: skip collections with a remote provider ID
            // unless the user has explicitly opted in to deleting those too.
            if (!includeWithProviderId)
            {
                var hasTmdbId = !string.IsNullOrEmpty(
                    boxSet.GetProviderId(MetadataProvider.Tmdb));
                var hasImdbId = !string.IsNullOrEmpty(
                    boxSet.GetProviderId(MetadataProvider.Imdb));

                if (hasTmdbId || hasImdbId)
                {
                    _logger.LogDebug(
                        "Skipping deletion of collection '{SetName}' — it has a remote provider ID",
                        boxSet.Name);
                    continue;
                }
            }

            _logger.LogInformation("Deleting orphaned collection '{SetName}'", boxSet.Name);

            try
            {
                _libraryManager.DeleteItem(boxSet, new DeleteOptions
                {
                    DeleteFileLocation = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete orphaned collection '{SetName}'", boxSet.Name);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Temporary cleanup of previously plugin-modified movie sort titles.
    /// </summary>
    private async Task CleanupLegacySortTitlesAsync(List<Movie> allMovies, CancellationToken cancellationToken)
    {
        foreach (var movie in allMovies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLegacyPluginSortName(movie.SortName, movie.Name))
            {
                _logger.LogInformation("Reverting legacy sort title for '{MovieName}'", movie.Name);
                movie.SortName = string.Empty;
                try
                {
                    await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to revert legacy sort title for '{MovieName}'", movie.Name);
                }
            }
        }
    }

    private static bool IsLegacyPluginSortName(string sortName, string movieName)
    {
        if (string.IsNullOrWhiteSpace(sortName) || string.IsNullOrWhiteSpace(movieName))
            return false;

        var suffix = " - " + movieName;
        if (!sortName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var prefix = sortName.Substring(0, sortName.Length - suffix.Length).Trim();
        if (prefix.Length < 3)
            return false;

        var lastSpaceIndex = prefix.LastIndexOf(' ');
        if (lastSpaceIndex <= 0)
            return false;

        var indexStr = prefix.Substring(lastSpaceIndex + 1);
        return indexStr.Length == 2 && int.TryParse(indexStr, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _debounceTimer.Dispose();
            _syncLock.Dispose();
        }

        _disposed = true;
    }
}
