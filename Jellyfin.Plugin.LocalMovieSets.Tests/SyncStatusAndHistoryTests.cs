using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class SyncStatusAndHistoryTests
{
    [Fact]
    public void DurationSeconds_CalculatesCorrectDifference()
    {
        var start = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(42.5);

        var status = new SyncStatusInfo
        {
            LastRunStartedUtc = start,
            LastRunCompletedUtc = end
        };

        Assert.Equal(42.5, status.DurationSeconds);
    }

    [Fact]
    public void DurationSeconds_ReturnsNullWhenTimestampsMissing()
    {
        var status = new SyncStatusInfo
        {
            LastRunStartedUtc = DateTime.UtcNow,
            LastRunCompletedUtc = null
        };

        Assert.Null(status.DurationSeconds);

        status.LastRunStartedUtc = null;
        Assert.Null(status.DurationSeconds);
    }

    [Fact]
    public void DurationSeconds_DoesNotReturnNegativeIfClockSkew()
    {
        var start = new DateTime(2026, 9, 29, 12, 0, 10, DateTimeKind.Utc);
        var end = start.AddSeconds(-5); // End earlier than start

        var status = new SyncStatusInfo
        {
            LastRunStartedUtc = start,
            LastRunCompletedUtc = end
        };

        Assert.Equal(0, status.DurationSeconds);
    }

    [Fact]
    public void Clone_CreatesIndependentHistoryCopy()
    {
        var original = new SyncStatusInfo
        {
            MoviesScanned = 100,
            History = new List<SyncStatusInfo>
            {
                new() { MoviesScanned = 50 },
                new() { MoviesScanned = 60 }
            }
        };

        var clone = original.Clone();

        Assert.Equal(original.MoviesScanned, clone.MoviesScanned);
        Assert.Equal(2, clone.History.Count);

        // Modifying clone's history should not modify original's history list
        var modifiable = new List<SyncStatusInfo>(clone.History)
        {
            new() { MoviesScanned = 70 }
        };
        clone.History = modifiable;

        Assert.Equal(2, original.History.Count);
        Assert.Equal(3, clone.History.Count);
    }

    [Fact]
    public void SyncHistory_JsonSerialization_RoundtripsSuccessfully()
    {
        var history = new List<SyncStatusInfo>
        {
            new()
            {
                LastRunStartedUtc = DateTime.UtcNow.AddMinutes(-10),
                LastRunCompletedUtc = DateTime.UtcNow.AddMinutes(-9).AddSeconds(15),
                LastRunOutcome = SyncOutcomes.Success,
                MoviesScanned = 1250,
                MoviesInSets = 430,
                SetsFound = 120,
                CollectionsCreated = 2,
                CollectionsUpdated = 5,
                CollectionsDeleted = 0,
                NfoParseErrors = 0
            },
            new()
            {
                LastRunStartedUtc = DateTime.UtcNow.AddHours(-1),
                LastRunCompletedUtc = DateTime.UtcNow.AddHours(-1).AddSeconds(20),
                LastRunOutcome = SyncOutcomes.Failed,
                MoviesScanned = 500,
                LastErrorMessage = "Network connection timeout"
            }
        };

        var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        Assert.NotEmpty(json);

        var deserialized = JsonSerializer.Deserialize<List<SyncStatusInfo>>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);

        Assert.Equal(SyncOutcomes.Success, deserialized[0].LastRunOutcome);
        Assert.Equal(1250, deserialized[0].MoviesScanned);
        Assert.Equal(75.0, deserialized[0].DurationSeconds);

        Assert.Equal(SyncOutcomes.Failed, deserialized[1].LastRunOutcome);
        Assert.Equal("Network connection timeout", deserialized[1].LastErrorMessage);
        Assert.Equal(20.0, deserialized[1].DurationSeconds);
    }

    [Fact]
    public void SyncHistory_CappedAt10Entries()
    {
        const int maxEntries = 10;
        var list = new List<SyncStatusInfo>();

        for (int i = 1; i <= 15; i++)
        {
            var entry = new SyncStatusInfo
            {
                MoviesScanned = i,
                History = Array.Empty<SyncStatusInfo>()
            };

            list.Insert(0, entry);
            while (list.Count > maxEntries)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        Assert.Equal(10, list.Count);
        // Latest inserted should be at index 0 (MoviesScanned = 15)
        Assert.Equal(15, list[0].MoviesScanned);
        // Oldest remaining should be at index 9 (MoviesScanned = 6)
        Assert.Equal(6, list[9].MoviesScanned);
    }

    [Fact]
    public void SyncPreviewResult_JsonSerialization_RoundtripsSuccessfully()
    {
        var preview = new SyncPreviewResult
        {
            ScannedMoviesCount = 1248,
            TotalSetsCount = 110,
            ScannedLibrariesCount = 2,
            UnchangedCount = 108,
            UnchangedSets =
            [
                new PreviewSetInfo { Name = "Alien Collection", MovieCount = 6 },
                new PreviewSetInfo { Name = "Star Wars Collection", MovieCount = 9 }
            ],
            ToCreate =
            [
                new PreviewSetInfo { Name = "Dune Collection", MovieCount = 2 }
            ],
            ToUpdate =
            [
                new PreviewUpdateInfo
                {
                    Name = "Spider-Man Collection",
                    MoviesToAdd = 1,
                    MoviesToRemove = 0,
                    AddedMovieTitles = ["Spider-Man: No Way Home (2021)"],
                    RemovedMovieTitles = []
                }
            ],
            ToDelete = ["Old Collection"]
        };

        var json = JsonSerializer.Serialize(preview);
        var deserialized = JsonSerializer.Deserialize<SyncPreviewResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1248, deserialized.ScannedMoviesCount);
        Assert.Equal(110, deserialized.TotalSetsCount);
        Assert.Equal(2, deserialized.ScannedLibrariesCount);
        Assert.Equal(108, deserialized.UnchangedCount);
        Assert.Equal(2, deserialized.UnchangedSets.Count);
        Assert.Equal("Alien Collection", deserialized.UnchangedSets[0].Name);
        Assert.Single(deserialized.ToCreate);
        Assert.Single(deserialized.ToUpdate);
        Assert.Equal("Spider-Man Collection", deserialized.ToUpdate[0].Name);
        Assert.Single(deserialized.ToUpdate[0].AddedMovieTitles);
        Assert.Equal("Spider-Man: No Way Home (2021)", deserialized.ToUpdate[0].AddedMovieTitles[0]);
        Assert.Single(deserialized.ToDelete);
    }
}

