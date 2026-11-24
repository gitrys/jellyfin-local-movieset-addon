using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

[Collection(PluginStateCollection.Name)]
public sealed class ForceRebuildReportingTests : IDisposable
{
    private readonly SyncTestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private (LocalMovieSetManager Manager, TestActivityManager Activity) CreateManager()
    {
        return (_harness.Manager, _harness.Activity);
    }

    [Fact]
    public async Task ForceRebuild_MountGuardAborted_LogsAbortWithoutDeletes()
    {
        var (manager, activity) = CreateManager();

        await manager.LogServerActivityAsync(new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.MountGuardAborted,
            RunKind = SyncRunKinds.ForceRebuild
        });

        var entry = Assert.Single(activity.CreatedEntries);
        Assert.Equal("Local Movie Sets Force Rebuild Aborted", entry.Name);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.Contains("No collections were deleted", entry.Overview);
    }

    [Fact]
    public async Task ForceRebuild_SuccessWithDeleteFailures_LogsWarning()
    {
        var (manager, activity) = CreateManager();

        await manager.LogServerActivityAsync(new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Success,
            RunKind = SyncRunKinds.ForceRebuildAll,
            RebuildDeleted = 10,
            RebuildDeleteFailures = 2,
            SetsFound = 12
        });

        var entry = Assert.Single(activity.CreatedEntries);
        Assert.Equal("Local Movie Sets Force Rebuild (all collections) Succeeded", entry.Name);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.Contains("Deleted 10 collection(s)", entry.Overview);
        Assert.Contains("2 collection(s) could not be deleted", entry.Overview);
    }

    [Fact]
    public async Task Sync_NfoReRead_ReportsReReadAndChangedCounts()
    {
        var (manager, activity) = CreateManager();

        await manager.LogServerActivityAsync(new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Success,
            MoviesRefreshedFromNfo = 3,
            MoviesUpdatedFromNfo = 1
        });

        var entry = Assert.Single(activity.CreatedEntries);
        Assert.Contains("Re-read 3 newer movie NFO(s), 1 with changes.", entry.Overview);
    }

    [Fact]
    public async Task ForceRebuild_FailedAfterDeletes_PointsToSyncNow()
    {
        var (manager, activity) = CreateManager();

        await manager.LogServerActivityAsync(new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Failed,
            RunKind = SyncRunKinds.ForceRebuild,
            RebuildDeleted = 3,
            LastErrorMessage = "Database locked"
        });

        var entry = Assert.Single(activity.CreatedEntries);
        Assert.Equal("Local Movie Sets Force Rebuild Failed", entry.Name);
        Assert.Contains("Database locked", entry.Overview);
        Assert.Contains("run Sync Now", entry.Overview);
    }

    [Fact]
    public void NewFields_SurviveJsonRoundtrip()
    {
        var original = new List<SyncStatusInfo>
        {
            new()
            {
                LastRunOutcome = SyncOutcomes.Success,
                RunKind = SyncRunKinds.ForceRebuild,
                RebuildDeleted = 7,
                RebuildDeleteFailures = 1
            }
        };

        var restored = JsonSerializer.Deserialize<List<SyncStatusInfo>>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        var run = Assert.Single(restored);
        Assert.Equal(SyncRunKinds.ForceRebuild, run.RunKind);
        Assert.Equal(7, run.RebuildDeleted);
        Assert.Equal(1, run.RebuildDeleteFailures);
    }

    [Fact]
    public void OldHistoryWithoutNewFields_LoadsWithDefaults()
    {
        const string oldJson = """[{"LastRunOutcome":"Success","SetsFound":3}]""";

        var restored = JsonSerializer.Deserialize<List<SyncStatusInfo>>(oldJson);

        Assert.NotNull(restored);
        var run = Assert.Single(restored);
        Assert.Equal(SyncRunKinds.Sync, run.RunKind);
        Assert.Equal(0, run.RebuildDeleted);
        Assert.Equal(0, run.RebuildDeleteFailures);
    }
}
