using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Queries;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Activity;
using Jellyfin.Data.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class TestActivityManager : IActivityManager
{
    public List<ActivityLog> CreatedEntries { get; } = new();

    public event EventHandler<GenericEventArgs<ActivityLogEntry>>? EntryCreated
    {
        add { }
        remove { }
    }

    public Task CreateAsync(ActivityLog entry)
    {
        CreatedEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<QueryResult<ActivityLogEntry>> GetPagedResultAsync(ActivityLogQuery query)
    {
        return Task.FromResult(new QueryResult<ActivityLogEntry>());
    }

    public Task CleanAsync(DateTime startDate)
    {
        return Task.CompletedTask;
    }
}

public class ActivityLogTests
{
    [Fact]
    public async Task LogServerActivityAsync_OnSuccess_CreatesInformationLog()
    {
        // Arrange
        var testActivityMgr = new TestActivityManager();
        var manager = new LocalMovieSetManager(
            null!,
            null!,
            null!,
            null!,
            new MovieNfoParser(NullLogger<MovieNfoParser>.Instance),
            NullLogger<LocalMovieSetManager>.Instance,
            testActivityMgr);

        var snapshot = new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Success,
            LastRunStartedUtc = DateTime.UtcNow.AddSeconds(-15),
            LastRunCompletedUtc = DateTime.UtcNow,
            SetsFound = 42,
            CollectionsCreated = 2,
            CollectionsUpdated = 5,
            CollectionsDeleted = 1,
            MoviesScanned = 150
        };

        // Act
        await manager.LogServerActivityAsync(snapshot);

        // Assert
        Assert.Single(testActivityMgr.CreatedEntries);
        var entry = testActivityMgr.CreatedEntries[0];
        Assert.Equal("Local Movie Sets Sync Succeeded", entry.Name);
        Assert.Equal("LocalMovieSets", entry.Type);
        Assert.Equal(LogLevel.Information, entry.LogSeverity);
        Assert.Contains("Synced 42 collections (+2 / ~5 / -1) from 150 movies", entry.Overview);
    }

    [Fact]
    public async Task LogServerActivityAsync_OnMountGuardAborted_CreatesWarningLog()
    {
        // Arrange
        var testActivityMgr = new TestActivityManager();
        var manager = new LocalMovieSetManager(
            null!,
            null!,
            null!,
            null!,
            new MovieNfoParser(NullLogger<MovieNfoParser>.Instance),
            NullLogger<LocalMovieSetManager>.Instance,
            testActivityMgr);

        var snapshot = new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.MountGuardAborted,
            LastRunStartedUtc = DateTime.UtcNow.AddSeconds(-1),
            LastRunCompletedUtc = DateTime.UtcNow
        };

        // Act
        await manager.LogServerActivityAsync(snapshot);

        // Assert
        Assert.Single(testActivityMgr.CreatedEntries);
        var entry = testActivityMgr.CreatedEntries[0];
        Assert.Equal("Local Movie Sets Sync Aborted", entry.Name);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.Contains("Mount Guard active", entry.Overview);
    }

    [Fact]
    public async Task LogServerActivityAsync_OnFailure_CreatesErrorLog()
    {
        // Arrange
        var testActivityMgr = new TestActivityManager();
        var manager = new LocalMovieSetManager(
            null!,
            null!,
            null!,
            null!,
            new MovieNfoParser(NullLogger<MovieNfoParser>.Instance),
            NullLogger<LocalMovieSetManager>.Instance,
            testActivityMgr);

        var snapshot = new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Failed,
            LastErrorMessage = "Database connection timed out",
            LastRunStartedUtc = DateTime.UtcNow.AddSeconds(-2),
            LastRunCompletedUtc = DateTime.UtcNow
        };

        // Act
        await manager.LogServerActivityAsync(snapshot);

        // Assert
        Assert.Single(testActivityMgr.CreatedEntries);
        var entry = testActivityMgr.CreatedEntries[0];
        Assert.Equal("Local Movie Sets Sync Failed", entry.Name);
        Assert.Equal(LogLevel.Error, entry.LogSeverity);
        Assert.Contains("Database connection timed out", entry.Overview);
    }

    [Fact]
    public async Task LogServerActivityAsync_NullActivityManager_DoesNotThrow()
    {
        // Arrange
        var manager = new LocalMovieSetManager(
            null!,
            null!,
            null!,
            null!,
            new MovieNfoParser(NullLogger<MovieNfoParser>.Instance),
            NullLogger<LocalMovieSetManager>.Instance,
            null);

        var snapshot = new SyncStatusInfo
        {
            LastRunOutcome = SyncOutcomes.Success
        };

        // Act & Assert
        var ex = await Record.ExceptionAsync(() => manager.LogServerActivityAsync(snapshot));
        Assert.Null(ex);
    }
}
