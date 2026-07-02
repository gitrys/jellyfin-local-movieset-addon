using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Core plugin service that:
/// <list type="bullet">
///   <item>Subscribes to Jellyfin library events (via <see cref="IHostedService"/>)</item>
///   <item>Scans movie NFO files for <c>&lt;set&gt;</c> tags</item>
///   <item>Creates or updates Jellyfin BoxSet collections via <see cref="ICollectionManager"/></item>
///   <item>Queues metadata refreshes so the plugin's providers apply set metadata and artwork
///   inside Jellyfin's refresh pipeline (no direct item saves)</item>
/// </list>
/// </summary>
public class LocalMovieSetManager : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly MovieNfoParser _movieNfoParser;
    private readonly ILogger<LocalMovieSetManager> _logger;

    // Debounce timer: fires 30 seconds after the last library change event
    private readonly Timer _debounceTimer;

    // Prevents concurrent syncs (debounce timer vs scheduled task)
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    // Cancelled on StopAsync/Dispose so background syncs stop at shutdown
    private readonly CancellationTokenSource _shutdownCts = new();

    // True while a sync is running, so the plugin's own repository updates
    // don't re-trigger the debounce timer via ItemUpdated events.
    private volatile bool _isSyncing;

    // Snapshot of the most recent sync run (in-memory only; resets on restart).
    // Replaced at the start of each run and mutated only while _syncLock is held.
    private SyncStatusInfo _lastStatus = new();

    // Set names discovered by the most recent sync. Consulted by the metadata
    // provider so it only touches collections this plugin manages. Replaced
    // atomically; never mutated in place.
    private volatile HashSet<string> _managedSetNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetManager"/> class.
    /// </summary>
    public LocalMovieSetManager(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        MovieNfoParser movieNfoParser,
        ILogger<LocalMovieSetManager> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _movieNfoParser = movieNfoParser;
        _logger = logger;

        // Timer starts stopped (Timeout.Infinite = never fire)
        _debounceTimer = new Timer(
            _ => _ = ScanAndSyncAsync(_shutdownCts.Token),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// Gets a value indicating whether a sync or rebuild is currently running.
    /// </summary>
    public bool IsSyncRunning => _syncLock.CurrentCount == 0;

    /// <summary>
    /// Returns whether the given collection name matches a set discovered by
    /// the most recent sync. Used by the metadata provider to avoid touching
    /// collections this plugin does not manage.
    /// </summary>
    /// <param name="setName">Collection/set name to check.</param>
    /// <returns><c>true</c> if the name belongs to a managed set.</returns>
    public bool IsManagedSet(string setName) => _managedSetNames.Contains(setName);

    /// <summary>
    /// Returns a snapshot of the most recent sync run's status and statistics.
    /// </summary>
    /// <returns>A copy of the current status.</returns>
    public SyncStatusInfo GetStatusSnapshot()
    {
        var snapshot = _lastStatus.Clone();
        snapshot.IsRunning = IsSyncRunning;
        return snapshot;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _libraryManager.ItemRemoved += OnLibraryItemChanged;
        _logger.LogInformation("Local Movie Sets: library event listeners registered");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        _libraryManager.ItemRemoved -= OnLibraryItemChanged;
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Cancel any background sync started by the debounce timer
        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a library item is added, updated or removed.
    /// Resets the debounce timer so a sync fires 30 seconds after the last event.
    /// Events caused by the plugin's own updates during a sync are ignored.
    /// </summary>
    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        if (e.Item is Movie)
        {
            _debounceTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Main sync entry point. Acquires the sync lock, then scans all movies,
    /// groups them by set name from their NFO files, and creates/updates
    /// Jellyfin BoxSet collections to match.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Optional progress reporter (0-100), used by the scheduled task.</param>
    public async Task ScanAndSyncAsync(CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        bool acquired;
        try
        {
            // Prevent overlapping syncs (debounce timer vs scheduled task)
            acquired = await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // Shutdown race: the manager was disposed while a timer callback fired
            return;
        }

        if (!acquired)
        {
            _logger.LogInformation("Local Movie Sets: sync already in progress, skipping");
            return;
        }

        try
        {
            await ScanAndSyncCoreAsync(cancellationToken, progress).ConfigureAwait(false);
        }
        finally
        {
            ReleaseSyncLockSafe();
        }
    }

    /// <summary>
    /// Performs the actual scan and sync. Assumes the caller holds <see cref="_syncLock"/>.
    /// Never throws: cancellation and errors are logged.
    /// </summary>
    private async Task ScanAndSyncCoreAsync(CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        _isSyncing = true;

        var stats = new SyncStatusInfo
        {
            LastRunStartedUtc = DateTime.UtcNow,
            LastRunOutcome = SyncOutcomes.Running
        };
        _lastStatus = stats;

        try
        {
            _logger.LogInformation("Local Movie Sets: starting sync");

            var config = Plugin.Instance?.Configuration;
            if (config is null)
            {
                _logger.LogError("Plugin configuration is not available");
                stats.LastRunOutcome = SyncOutcomes.Failed;
                stats.LastErrorMessage = "Plugin configuration is not available";
                return;
            }

            if (config.EnableMountGuard && !CheckMounts())
            {
                _logger.LogWarning("Local Movie Sets: sync aborted due to offline or empty library paths (Mount Guard active).");
                stats.LastRunOutcome = SyncOutcomes.MountGuardAborted;
                return;
            }

            // The image provider only runs when it is enabled as an image
            // fetcher in the collections library options, so make sure it is.
            EnsureImageFetcherEnabled();

            // ── Step 1: Query all movies ──────────────────────────────────────
            var allMovies = QueryAllMovies();
            stats.MoviesScanned = allMovies.Count;

            _logger.LogInformation("Local Movie Sets: scanning {Count} movies", allMovies.Count);
            progress?.Report(5);

            // ── Step 2: Parse NFOs — group movies by set name ─────────────────
            var setGroups = BuildSetGroups(allMovies, cancellationToken, stats);
            stats.SetsFound = setGroups.Count;
            stats.MoviesInSets = setGroups.Values.Sum(g => g.Count);

            // Publish the managed set names for the metadata/image providers
            _managedSetNames = new HashSet<string>(setGroups.Keys, StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Local Movie Sets: found {SetCount} distinct sets across {MovieCount} movies",
                stats.SetsFound,
                stats.MoviesInSets);
            progress?.Report(15);

            // ── Step 3: Load existing BoxSet collections ───────────────────────
            var (existingBoxSets, existingByName, duplicateNames) = LoadExistingBoxSets();
            stats.DuplicateCollectionNames = duplicateNames;

            if (duplicateNames.Count > 0)
            {
                _logger.LogWarning(
                    "Local Movie Sets: multiple collections share the same name ({Names}). Only the first of each will be managed; consider removing the duplicates.",
                    string.Join(", ", duplicateNames));
            }

            // ── Step 4: Create or update collections ───────────────────────────
            var processedSets = 0;
            foreach (var (setName, movies) in setGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (existingByName.TryGetValue(setName, out var existingBoxSet))
                {
                    // Existing collections are always kept in sync, even below the
                    // minimum, so they don't freeze in a stale state.
                    var updated = await UpdateExistingCollectionAsync(existingBoxSet, movies, setName, config, cancellationToken)
                        .ConfigureAwait(false);
                    if (updated)
                    {
                        stats.CollectionsUpdated++;
                    }
                }
                else
                {
                    var minimumMovies = Math.Max(1, config.MinimumMovies);
                    if (movies.Count < minimumMovies)
                    {
                        _logger.LogDebug(
                            "Not creating collection for set '{SetName}': {Count} movies < minimum {Min}",
                            setName, movies.Count, config.MinimumMovies);
                        processedSets++;
                        continue;
                    }

                    var created = await CreateNewCollectionAsync(setName, movies, config, cancellationToken)
                        .ConfigureAwait(false);
                    if (created)
                    {
                        stats.CollectionsCreated++;
                    }
                }

                processedSets++;
                if (setGroups.Count > 0)
                {
                    progress?.Report(15 + (70.0 * processedSets / setGroups.Count));
                }

                // Spacing delay to let Jellyfin's file system watcher settle
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }

            // ── Step 5: Optionally remove orphaned collections ─────────────────
            if (config.DeleteOrphanedSets)
            {
                stats.CollectionsDeleted = await DeleteOrphanedSetsAsync(existingBoxSets, setGroups, config.DeleteSetsWithProviderId, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(90);

            // ── Step 6: One-time cleanup of legacy sort titles ──────────────────
            if (!config.LegacySortTitleCleanupCompleted)
            {
                await CleanupLegacySortTitlesAsync(allMovies, cancellationToken).ConfigureAwait(false);

                config.LegacySortTitleCleanupCompleted = true;
                Plugin.Instance?.SaveConfiguration();
            }

            stats.LastRunOutcome = SyncOutcomes.Success;
            progress?.Report(100);
            _logger.LogInformation("Local Movie Sets: sync completed successfully");
        }
        catch (OperationCanceledException)
        {
            stats.LastRunOutcome = SyncOutcomes.Cancelled;
            _logger.LogInformation("Local Movie Sets: sync was cancelled");
        }
        catch (Exception ex)
        {
            stats.LastRunOutcome = SyncOutcomes.Failed;
            stats.LastErrorMessage = ex.Message;
            _logger.LogError(ex, "Local Movie Sets: sync failed with an unexpected error");
        }
        finally
        {
            stats.LastRunCompletedUtc = DateTime.UtcNow;
            _isSyncing = false;
        }
    }

    /// <summary>
    /// Queries all non-virtual movies from the library.
    /// </summary>
    private List<Movie> QueryAllMovies()
    {
        return _libraryManager
            .GetItemsResult(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                IsVirtualItem = false
            })
            .Items
            .OfType<Movie>()
            .ToList();
    }

    /// <summary>
    /// Parses each movie's NFO and groups movies by set name (case-insensitive).
    /// </summary>
    /// <param name="allMovies">Movies to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="stats">Optional stats object; NFO parse errors are counted on it.</param>
    private Dictionary<string, List<Movie>> BuildSetGroups(
        List<Movie> allMovies,
        CancellationToken cancellationToken,
        SyncStatusInfo? stats = null)
    {
        var setGroups = new Dictionary<string, List<Movie>>(StringComparer.OrdinalIgnoreCase);

        foreach (var movie in allMovies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(movie.Path))
                continue;

            var outcome = _movieNfoParser.ParseNfo(movie.Path);
            if (outcome.HadError && stats != null)
            {
                stats.NfoParseErrors++;
            }

            if (outcome.Membership is null)
                continue;

            if (!setGroups.TryGetValue(outcome.Membership.SetName, out var group))
            {
                group = [];
                setGroups[outcome.Membership.SetName] = group;
            }

            group.Add(movie);
        }

        return setGroups;
    }

    /// <summary>
    /// Loads all BoxSet collections and builds a name lookup. Jellyfin can contain
    /// multiple collections with the same name (e.g. leftovers from other plugins);
    /// only the first of each name is managed, and duplicates are reported.
    /// </summary>
    private (List<BoxSet> All, Dictionary<string, BoxSet> ByName, List<string> DuplicateNames) LoadExistingBoxSets()
    {
        var existingBoxSets = _libraryManager
            .GetItemsResult(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.BoxSet]
            })
            .Items
            .OfType<BoxSet>()
            .ToList();

        var groups = existingBoxSets
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingByName = groups
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var duplicateNames = groups
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return (existingBoxSets, existingByName, duplicateNames);
    }

    /// <summary>
    /// Computes the membership difference between a collection's current linked
    /// movies and the target movie list.
    /// </summary>
    private static (Guid[] ToAdd, Guid[] ToRemove, List<Movie> ExistingMovies) ComputeMembershipDiff(
        BoxSet boxSet,
        List<Movie> targetMovies)
    {
        var existingMovieItems = boxSet
            .GetLinkedChildren()
            .OfType<Movie>()
            .ToList();

        var existingMovieIds = existingMovieItems
            .Select(c => c.Id)
            .ToHashSet();

        var targetMovieIds = targetMovies
            .Select(m => m.Id)
            .ToHashSet();

        var toAdd = targetMovies
            .Where(m => !existingMovieIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToArray();

        var toRemove = existingMovieIds
            .Where(id => !targetMovieIds.Contains(id))
            .ToArray();

        return (toAdd, toRemove, existingMovieItems);
    }

    /// <summary>
    /// Read-only dry run: computes what the next sync would do under the
    /// currently saved settings, without modifying anything.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preview result.</returns>
    public SyncPreviewResult Preview(CancellationToken cancellationToken)
    {
        var result = new SyncPreviewResult();

        if (IsSyncRunning)
        {
            result.SyncRunning = true;
            return result;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            result.ErrorMessage = "Plugin configuration is not available.";
            return result;
        }

        if (config.EnableMountGuard && !CheckMounts())
        {
            result.MountGuardBlocked = true;
            return result;
        }

        var allMovies = QueryAllMovies();
        var setGroups = BuildSetGroups(allMovies, cancellationToken);
        var (existingBoxSets, existingByName, _) = LoadExistingBoxSets();
        var minimumMovies = Math.Max(1, config.MinimumMovies);

        foreach (var (setName, movies) in setGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingByName.TryGetValue(setName, out var boxSet))
            {
                var sortedMovies = SortMovies(movies, config.CollectionSortBy, config.CollectionSortOrder);
                var (toAdd, toRemove, _) = ComputeMembershipDiff(boxSet, sortedMovies);

                if (toAdd.Length == 0 && toRemove.Length == 0)
                {
                    result.UnchangedCount++;
                }
                else
                {
                    result.ToUpdate.Add(new PreviewUpdateInfo
                    {
                        Name = setName,
                        MoviesToAdd = toAdd.Length,
                        MoviesToRemove = toRemove.Length
                    });
                }
            }
            else if (movies.Count >= minimumMovies)
            {
                result.ToCreate.Add(new PreviewSetInfo { Name = setName, MovieCount = movies.Count });
            }
            else
            {
                result.BelowMinimum.Add(new PreviewSetInfo { Name = setName, MovieCount = movies.Count });
            }
        }

        foreach (var boxSet in existingBoxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (setGroups.ContainsKey(boxSet.Name))
                continue;

            if (!config.DeleteOrphanedSets)
            {
                result.ProtectedOrphans.Add(new PreviewProtectedInfo
                {
                    Name = boxSet.Name,
                    Reason = "Orphan deletion is disabled"
                });
                continue;
            }

            if (!config.DeleteSetsWithProviderId)
            {
                var hasProviderId =
                    !string.IsNullOrEmpty(boxSet.GetProviderId(MetadataProvider.Tmdb))
                    || !string.IsNullOrEmpty(boxSet.GetProviderId(MetadataProvider.Imdb));

                if (hasProviderId)
                {
                    result.ProtectedOrphans.Add(new PreviewProtectedInfo
                    {
                        Name = boxSet.Name,
                        Reason = "Has a TMDB/IMDb provider ID"
                    });
                    continue;
                }
            }

            result.ToDelete.Add(boxSet.Name);
        }

        return result;
    }

    /// <summary>
    /// Releases the sync lock, tolerating the shutdown race where the semaphore
    /// has already been disposed while a background sync was still running.
    /// </summary>
    private void ReleaseSyncLockSafe()
    {
        try
        {
            _syncLock.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Ensures the plugin's dynamic image provider is enabled as a BoxSet image
    /// fetcher in every collections library. Jellyfin only runs an
    /// <c>IDynamicImageProvider</c> when the library's per-type
    /// <c>ImageFetchers</c> list contains its name; a library configured with an
    /// empty list (e.g. after disabling TMDb/Fanart images for collections)
    /// silently disables this plugin's artwork too. Runs once per sync and only
    /// writes when a change is needed.
    /// </summary>
    private void EnsureImageFetcherEnabled()
    {
        try
        {
            // Library folders are children of the *user* root folder
            // (root/default), not the aggregate root folder.
            foreach (var libraryFolder in _libraryManager.GetUserRootFolder().Children.OfType<CollectionFolder>())
            {
                if (libraryFolder.CollectionType != CollectionType.boxsets)
                {
                    continue;
                }

                var options = libraryFolder.GetLibraryOptions();
                var typeOptions = options.TypeOptions?
                    .FirstOrDefault(t => string.Equals(t.Type, nameof(BoxSet), StringComparison.OrdinalIgnoreCase));

                // No per-type override: Jellyfin falls back to the server-wide
                // metadata options, where fetchers are enabled unless explicitly
                // disabled. Nothing to do.
                if (typeOptions is null)
                {
                    continue;
                }

                var fetchers = typeOptions.ImageFetchers ?? [];
                if (fetchers.Contains(BoxSetImageProvider.ProviderName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                typeOptions.ImageFetchers = [.. fetchers, BoxSetImageProvider.ProviderName];

                // Put the plugin first so local set artwork wins over remote
                // fetchers should the user (re-)enable those later.
                var order = typeOptions.ImageFetcherOrder ?? [];
                if (!order.Contains(BoxSetImageProvider.ProviderName, StringComparer.OrdinalIgnoreCase))
                {
                    typeOptions.ImageFetcherOrder = [BoxSetImageProvider.ProviderName, .. order];
                }

                libraryFolder.UpdateLibraryOptions(options);
                _logger.LogInformation(
                    "Local Movie Sets: enabled '{Provider}' as a BoxSet image fetcher for library '{Library}'",
                    BoxSetImageProvider.ProviderName,
                    libraryFolder.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local Movie Sets: could not verify/enable the image fetcher in library options");
        }
    }

    /// <summary>
    /// Checks configured movie library paths to verify they are online and not empty.
    /// Used by Mount Guard to prevent collection corruption due to offline network mounts.
    /// </summary>
    private bool CheckMounts()
    {
        var movieLibraries = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == MediaBrowser.Model.Entities.CollectionTypeOptions.movies);

        foreach (var library in movieLibraries)
        {
            if (library.Locations == null) continue;
            foreach (var path in library.Locations)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (!Directory.Exists(path))
                {
                    _logger.LogWarning("Mount Guard: Library path '{Path}' is offline or missing. Sync aborted to protect collections.", path);
                    return false;
                }

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(path).Any())
                    {
                        _logger.LogWarning("Mount Guard: Library path '{Path}' is empty. Sync aborted to protect collections.", path);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Mount Guard: Library path '{Path}' is inaccessible. Sync aborted to protect collections.", path);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Deletes ALL BoxSet collections in Jellyfin and triggers a clean scan and sync.
    /// The sync lock is held across both phases so no other sync can interleave.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a sync is already running or the mount guard blocks the rebuild.
    /// </exception>
    public async Task ForceRebuildAsync(CancellationToken cancellationToken)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Sync is already in progress. Please wait for it to complete.");
        }

        try
        {
            _isSyncing = true;

            _logger.LogInformation("Local Movie Sets: starting Force Rebuild");

            var config = Plugin.Instance?.Configuration;
            if (config != null && config.EnableMountGuard)
            {
                if (!CheckMounts())
                {
                    throw new InvalidOperationException("Force rebuild aborted because one or more library paths are offline/empty.");
                }
            }

            var existingBoxSets = _libraryManager
                .GetItemsResult(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.BoxSet]
                })
                .Items
                .OfType<BoxSet>()
                .ToList();

            _logger.LogInformation("Local Movie Sets: deleting {Count} existing collection(s)", existingBoxSets.Count);

            foreach (var boxSet in existingBoxSets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Delete the on-disk "[boxset]" folder too. It only contains
                    // Jellyfin-managed artwork and collection.xml (never media), and
                    // leaving it behind causes stale-folder collisions when a
                    // collection with the same name is recreated right after.
                    _libraryManager.DeleteItem(boxSet, new DeleteOptions
                    {
                        DeleteFileLocation = true
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete collection '{SetName}' during rebuild", boxSet.Name);
                }
            }

            _logger.LogInformation("Local Movie Sets: finished deleting collections, starting fresh sync");

            // Fresh scan and sync while still holding the lock, so the debounce
            // timer or scheduled task can't steal it between the two phases.
            await ScanAndSyncCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isSyncing = false;
            ReleaseSyncLockSafe();
        }
    }

    private async Task UpdateRepositoryWithRetryAsync(
        BaseItem item,
        ItemUpdateType updateType,
        CancellationToken cancellationToken,
        int maxRetries = 3,
        int delayMs = 250)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await item.UpdateToRepositoryAsync(updateType, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "File lock encountered while updating repository for '{ItemName}'. Retrying {Attempt}/{MaxRetries} after {Delay}ms...", item.Name, i + 1, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        await item.UpdateToRepositoryAsync(updateType, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddToCollectionWithRetryAsync(
        Guid collectionId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken,
        int maxRetries = 3,
        int delayMs = 250)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _collectionManager.AddToCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
                return;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "File lock encountered during AddToCollectionAsync. Retrying {Attempt}/{MaxRetries} after {Delay}ms...", i + 1, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        await _collectionManager.AddToCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
    }

    private async Task RemoveFromCollectionWithRetryAsync(
        Guid collectionId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken,
        int maxRetries = 3,
        int delayMs = 250)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _collectionManager.RemoveFromCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
                return;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "File lock encountered during RemoveFromCollectionAsync. Retrying {Attempt}/{MaxRetries} after {Delay}ms...", i + 1, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        await _collectionManager.RemoveFromCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Queues a full metadata + image refresh for a collection. The plugin's
    /// <c>BoxSetMetadataProvider</c> and <c>BoxSetImageProvider</c> run inside
    /// that refresh, so all metadata and artwork writes happen in Jellyfin's
    /// serialized pipeline instead of racing it.
    /// </summary>
    /// <param name="collectionId">The BoxSet item id.</param>
    /// <param name="replaceAllImages">
    /// When <c>true</c>, existing images are replaced from the set folder;
    /// when <c>false</c>, only missing images are filled in.
    /// </param>
    private void QueueCollectionRefresh(Guid collectionId, bool replaceAllImages)
    {
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllImages = replaceAllImages
        };

        _providerManager.QueueRefresh(collectionId, options, RefreshPriority.High);
    }

    private async Task<bool> CreateNewCollectionAsync(
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
                return false;
            }

            // Force update on all movies in the new collection to refresh UI grouping cache
            foreach (var movie in sortedMovies)
            {
                try
                {
                    await UpdateRepositoryWithRetryAsync(movie, ItemUpdateType.MetadataEdit, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update movie '{MovieName}' after adding to new collection", movie.Name);
                }
            }

            // Metadata and artwork are applied by the plugin's providers inside
            // Jellyfin's refresh pipeline, where all item writes are serialized.
            // Nothing is saved directly here anymore.
            QueueCollectionRefresh(collection.Id, replaceAllImages: true);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection '{SetName}'", setName);
            return false;
        }
    }

    private async Task<bool> UpdateExistingCollectionAsync(
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

            var (newMovieIds, movieIdsToRemove, existingMovieItems) = ComputeMembershipDiff(boxSet, sortedMovies);

            if (newMovieIds.Length > 0)
            {
                _logger.LogInformation(
                    "Adding {Count} new movie(s) to collection '{SetName}'",
                    newMovieIds.Length, setName);

                await AddToCollectionWithRetryAsync(boxSet.Id, newMovieIds, cancellationToken)
                    .ConfigureAwait(false);

                // Force update on newly added movies to refresh UI grouping cache
                var newMovieItems = sortedMovies.Where(m => newMovieIds.Contains(m.Id)).ToList();
                foreach (var movie in newMovieItems)
                {
                    try
                    {
                        await UpdateRepositoryWithRetryAsync(movie, ItemUpdateType.MetadataEdit, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update movie '{MovieName}' after adding to collection", movie.Name);
                    }
                }
            }

            if (movieIdsToRemove.Length > 0)
            {
                _logger.LogInformation(
                    "Removing {Count} orphaned movie(s) from collection '{SetName}'",
                    movieIdsToRemove.Length, setName);

                await RemoveFromCollectionWithRetryAsync(boxSet.Id, movieIdsToRemove, cancellationToken)
                    .ConfigureAwait(false);

                // Force update on removed movies to show them outside the collection
                var removedMovieItems = existingMovieItems.Where(m => movieIdsToRemove.Contains(m.Id)).ToList();
                foreach (var movie in removedMovieItems)
                {
                    try
                    {
                        await UpdateRepositoryWithRetryAsync(movie, ItemUpdateType.MetadataEdit, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update movie '{MovieName}' after removing from collection", movie.Name);
                    }
                }
            }

            // Metadata (NFO fields, display order, aggregation) and artwork are
            // applied by the plugin's providers inside the queued refresh. The
            // refresh is queued on every sync so set NFO edits propagate; images
            // are only replaced when the user opted in via UpdateExistingArtwork
            // (missing images are always filled in).
            QueueCollectionRefresh(boxSet.Id, replaceAllImages: config.UpdateExistingArtwork);

            return newMovieIds.Length > 0 || movieIdsToRemove.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update collection '{SetName}'", setName);
            return false;
        }
    }

    /// <summary>
    /// Orders movies for the initial linked-children order of a collection.
    /// "SortName" sorts by sort title; everything else (Default, PremiereDate and
    /// legacy config values) sorts chronologically by premiere date.
    /// The sort direction only matters for "Creation Order" (Default) display mode.
    /// </summary>
    private static List<Movie> SortMovies(List<Movie> movies, string sortBy, string sortOrder)
    {
        var isDescending = string.Equals(sortOrder, "Descending", StringComparison.OrdinalIgnoreCase);

        Func<Movie, object?> keySelector = string.Equals(sortBy, "SortName", StringComparison.OrdinalIgnoreCase)
            ? m => m.SortName
            : m => m.PremiereDate ?? DateTime.MaxValue;

        return isDescending
            ? movies.OrderByDescending(keySelector).ThenByDescending(m => m.SortName).ToList()
            : movies.OrderBy(keySelector).ThenBy(m => m.SortName).ToList();
    }

    /// <summary>
    /// Deletes BoxSet collections that have no corresponding active set group.
    /// By default, only removes collections without a TMDB/IMDb provider ID.
    /// When <paramref name="includeWithProviderId"/> is true, all orphaned collections are deleted.
    /// </summary>
    /// <returns>The number of collections deleted.</returns>
    private Task<int> DeleteOrphanedSetsAsync(
        List<BoxSet> existingBoxSets,
        Dictionary<string, List<Movie>> activeSetGroups,
        bool includeWithProviderId,
        CancellationToken cancellationToken)
    {
        var deletedCount = 0;

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
                // Remove the on-disk "[boxset]" folder as well so orphaned
                // collections don't leave stale folders behind (matches the
                // behavior of deleting a collection through the Jellyfin UI).
                _libraryManager.DeleteItem(boxSet, new DeleteOptions
                {
                    DeleteFileLocation = true
                });
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete orphaned collection '{SetName}'", boxSet.Name);
            }
        }

        return Task.FromResult(deletedCount);
    }

    /// <summary>
    /// One-time cleanup of movie sort titles written by old plugin versions.
    /// Runs only until <see cref="PluginConfiguration.LegacySortTitleCleanupCompleted"/> is set.
    /// </summary>
    private async Task CleanupLegacySortTitlesAsync(List<Movie> allMovies, CancellationToken cancellationToken)
    {
        foreach (var movie in allMovies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLegacyPluginSortName(movie.SortName, movie.Name))
            {
                _logger.LogInformation("Reverting legacy sort title for '{MovieName}'", movie.Name);

                // Clear the forced sort name so Jellyfin recomputes the default,
                // instead of persisting an empty sort title.
                movie.ForcedSortName = null;
                try
                {
                    await UpdateRepositoryWithRetryAsync(movie, ItemUpdateType.MetadataEdit, cancellationToken)
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

            try
            {
                _shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _shutdownCts.Dispose();
            _syncLock.Dispose();
        }

        _disposed = true;
    }
}
