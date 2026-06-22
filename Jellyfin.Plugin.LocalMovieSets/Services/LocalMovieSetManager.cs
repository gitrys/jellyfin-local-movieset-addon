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
        _logger.LogInformation("Local Movie Sets: starting sync");

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.LogError("Plugin configuration is not available");
            return;
        }

        try
        {
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

                if (movies.Count < config.MinimumMovies)
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
                await DeleteOrphanedSetsAsync(existingBoxSets, setGroups, cancellationToken)
                    .ConfigureAwait(false);
            }

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
            var collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = setName,
                ItemIdList = movies.Select(m => m.Id.ToString()).ToArray()
            }).ConfigureAwait(false);

            if (collection is null)
            {
                _logger.LogWarning("CreateCollectionAsync returned null for '{SetName}'", setName);
                return;
            }

            await ApplySetMetadataAsync(collection, setName, config, cancellationToken)
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
        _logger.LogDebug("Updating existing collection '{SetName}'", setName);

        try
        {
            // Find movies not yet in the collection
            var existingChildIds = boxSet
                .GetLinkedChildren()
                .Select(c => c.Id)
                .ToHashSet();

            var newMovieIds = movies
                .Where(m => !existingChildIds.Contains(m.Id))
                .Select(m => m.Id)
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

    /// <summary>
    /// Reads the dedicated set NFO (if available) and applies overview/title to the BoxSet.
    /// </summary>
    private async Task ApplySetMetadataAsync(
        BoxSet collection,
        string setName,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.SetDataFolder))
            return;

        var setInfo = _setNfoParser.ParseSet(config.SetDataFolder, setName, config.NfoNaming);
        if (setInfo is null)
            return;

        var changed = false;

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
    /// Only removes collections that appear to have been created by this plugin
    /// (no embedded provider source ID from another provider like TMDB).
    /// </summary>
    private Task DeleteOrphanedSetsAsync(
        List<BoxSet> existingBoxSets,
        Dictionary<string, List<Movie>> activeSetGroups,
        CancellationToken cancellationToken)
    {
        foreach (var boxSet in existingBoxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (activeSetGroups.ContainsKey(boxSet.Name))
                continue;

            // Safety guard: only delete if the collection has no remote provider ID
            // to avoid removing collections the user created via other means (e.g. TMDB).
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
            _debounceTimer.Dispose();

        _disposed = true;
    }
}
