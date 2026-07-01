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
        "collections from local tinyMediaManager metadata. No internet access required.";

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
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Local Movie Sets scheduled task started");
        progress.Report(0);

        await _manager.ScanAndSyncAsync(cancellationToken, progress).ConfigureAwait(false);

        progress.Report(100);
        _logger.LogInformation("Local Movie Sets scheduled task finished");
    }
}
