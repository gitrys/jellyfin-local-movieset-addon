using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.ScheduledTasks;

/// <summary>
/// Jellyfin scheduled task that triggers a full library scan and collection sync.
/// Appears in Dashboard → Scheduled Tasks as "Sync Local Movie Sets".
/// Can also be triggered manually from the plugin's configuration page.
/// </summary>
public class SyncMovieSetsTask : IScheduledTask
{
    private readonly LocalMovieSetManager _manager;
    private readonly ILogger<SyncMovieSetsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncMovieSetsTask"/> class.
    /// </summary>
    /// <param name="manager">The core manager service (injected).</param>
    /// <param name="logger">Logger instance (injected).</param>
    public SyncMovieSetsTask(LocalMovieSetManager manager, ILogger<SyncMovieSetsTask> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync Local Movie Sets";

    /// <inheritdoc />
    /// <remarks>Must be unique across all plugins in the Jellyfin instance.</remarks>
    public string Key => "LocalMovieSets_Sync";

    /// <inheritdoc />
    public string Description =>
        "Scans movie NFO files for set tags and creates or updates Jellyfin " +
        "collections from local media manager metadata (e.g. tinyMediaManager, MediaElch, Kodi). No internet access required.";

    /// <inheritdoc />
    public string Category => "Local Movie Sets";

    /// <inheritdoc />
    /// <returns>A single interval trigger that fires every 24 hours.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Local Movie Sets scheduled task started");
        progress.Report(0);

        // Wait for a sync that is already running (e.g. the automatic one after a
        // library scan), then run this one, so "Sync Now" always performs a sync.
        var result = await _manager.ScanAndSyncAsync(cancellationToken, progress, waitIfRunning: true)
            .ConfigureAwait(false);

        // Jellyfin shows a task as failed when it throws, and as cancelled on
        // OperationCanceledException. Without this, every outcome showed "Completed".
        ThrowIfUnsuccessful(result, cancellationToken);

        progress.Report(100);
        _logger.LogInformation("Local Movie Sets scheduled task finished");
    }

    /// <summary>
    /// Turns an unsuccessful sync outcome into the exception Jellyfin expects from a scheduled task.
    /// </summary>
    /// <param name="result">The run's status, or <c>null</c> when it did not start.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <exception cref="OperationCanceledException">The sync or the task was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The sync failed or the Mount Guard aborted it.</exception>
    internal static void ThrowIfUnsuccessful(SyncStatusInfo? result, CancellationToken cancellationToken)
    {
        if (result is null)
        {
            // Not started: cancelled while waiting, or the server is shutting down.
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        switch (result.LastRunOutcome)
        {
            case SyncOutcomes.Cancelled:
                throw new OperationCanceledException("Local Movie Sets sync was cancelled.", cancellationToken);

            case SyncOutcomes.MountGuardAborted:
                throw new InvalidOperationException(
                    "Sync aborted by Mount Guard: a movie library path is offline, empty, or unreadable. Nothing was changed.");

            case SyncOutcomes.Failed:
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.LastErrorMessage)
                        ? "Sync failed with an unexpected error. See the server log."
                        : $"Sync failed: {result.LastErrorMessage}");
        }
    }
}
