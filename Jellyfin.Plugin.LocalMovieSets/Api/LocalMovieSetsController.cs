using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Api;

/// <summary>
/// Custom API controller for the Local Movie Sets plugin.
/// Jellyfin auto-discovers and registers this route at /LocalMovieSets/...
/// </summary>
[ApiController]
[Route("LocalMovieSets")]
[Authorize(Policy = "RequiresElevation")]
public class LocalMovieSetsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly LocalMovieSetManager _manager;
    private readonly Jellyfin.Plugin.LocalMovieSets.Services.Validation.NfoValidator _validator;
    private readonly MediaBrowser.Controller.IServerApplicationHost _applicationHost;
    private readonly ILogger<LocalMovieSetsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager (injected).</param>
    /// <param name="manager">LocalMovieSetManager instance (injected).</param>
    /// <param name="validator">NFO and artwork validator instance (injected).</param>
    /// <param name="applicationHost">Jellyfin server application host (injected).</param>
    /// <param name="logger">Logger instance (injected).</param>
    public LocalMovieSetsController(
        ILibraryManager libraryManager,
        LocalMovieSetManager manager,
        Jellyfin.Plugin.LocalMovieSets.Services.Validation.NfoValidator validator,
        MediaBrowser.Controller.IServerApplicationHost applicationHost,
        ILogger<LocalMovieSetsController> logger)
    {
        _libraryManager = libraryManager;
        _manager = manager;
        _validator = validator;
        _applicationHost = applicationHost;
        _logger = logger;
    }

    /// <summary>
    /// Returns the status and statistics of the most recent sync run.
    /// </summary>
    /// <returns>The current sync status snapshot.</returns>
    [HttpGet("Status")]
    public ActionResult<SyncStatusInfo> GetStatus()
    {
        return Ok(_manager.GetStatusSnapshot());
    }

    /// <summary>
    /// Clears the recorded sync history.
    /// </summary>
    /// <returns>NoContent on success.</returns>
    [HttpPost("ClearHistory")]
    public ActionResult ClearHistory()
    {
        _manager.ClearSyncHistory();
        return NoContent();
    }

    /// <summary>
    /// Scans the Movie Set Data Folder and runs diagnostic validation on NFO files and artwork.
    /// </summary>
    /// <param name="folderPath">Optional override folder path. If omitted, uses the configured SetDataFolder.</param>
    /// <returns>Validation report containing all diagnostic findings.</returns>
    [HttpGet("Validate")]
    public ActionResult<Jellyfin.Plugin.LocalMovieSets.Services.Validation.ValidationReportDto> Validate([FromQuery] string? folderPath = null)
    {
        try
        {
            var report = _validator.Validate(folderPath);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during NFO and artwork validation scan");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Computes a read-only preview of what the next sync would do
    /// under the currently saved settings. Nothing is modified.
    /// </summary>
    /// <returns>The preview result.</returns>
    [HttpGet("Preview")]
    public ActionResult<SyncPreviewResult> Preview(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(_manager.Preview(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview failed");
            return Ok(new SyncPreviewResult { ErrorMessage = ex.Message });
        }
    }

    /// <summary>
    /// Checks all libraries to see if "Automatically add to collection" is active.
    /// </summary>
    /// <returns>A validation result containing conflicting library names.</returns>
    [HttpGet("CheckConflicts")]
    public ActionResult<ConflictCheckResult> CheckConflicts()
    {
        var folders = _libraryManager.GetVirtualFolders();
        var conflictingLibraries = folders
            .Where(f => f.LibraryOptions != null && f.LibraryOptions.AutomaticallyAddToCollection)
            .Select(f => f.Name)
            .ToList();

        return Ok(new ConflictCheckResult
        {
            HasConflicts = conflictingLibraries.Count > 0,
            ConflictingLibraries = conflictingLibraries
        });
    }

    /// <summary>
    /// Checks whether the collections library enables only this plugin as a BoxSet image fetcher.
    /// </summary>
    /// <returns>A conflict when online or other image fetchers are still enabled, or when no BoxSet list is saved.</returns>
    [HttpGet("CheckImageFetchers")]
    public ActionResult<ImageFetcherCheckResult> CheckImageFetchers()
    {
        var collectionsLibraries = _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>()
            .Where(folder => folder.CollectionType == CollectionType.boxsets)
            .ToList();

        if (collectionsLibraries.Count == 0)
        {
            return Ok(new ImageFetcherCheckResult
            {
                HasConflict = true,
                Reason = ImageFetcherCheck.NoCollectionsLibrary
            });
        }

        foreach (var library in collectionsLibraries)
        {
            var typeOptions = library.GetLibraryOptions().TypeOptions?
                .FirstOrDefault(option => string.Equals(option.Type, "BoxSet", StringComparison.OrdinalIgnoreCase));

            var result = ImageFetcherCheck.Evaluate(typeOptions is not null, typeOptions?.ImageFetchers);
            if (!result.HasConflict)
            {
                continue;
            }

            result.LibraryName = library.Name;
            return Ok(result);
        }

        return Ok(new ImageFetcherCheckResult { HasConflict = false });
    }

    /// <summary>
    /// Returns the list of all available movie libraries in Jellyfin.
    /// </summary>
    /// <returns>List of movie libraries with their ID and Name.</returns>
    [HttpGet("Libraries")]
    public ActionResult<List<LibraryInfoDto>> GetLibraries()
    {
        var movieLibraries = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == MediaBrowser.Model.Entities.CollectionTypeOptions.movies ||
                        string.Equals(f.CollectionType?.ToString(), "movies", StringComparison.OrdinalIgnoreCase))
            .Select(f => new LibraryInfoDto
            {
                Id = f.ItemId,
                Name = f.Name,
                Locations = f.Locations ?? []
            })
            .ToList();

        return Ok(movieLibraries);
    }

    /// <summary>
    /// Auto-detects the NFO naming convention used in the specified set folder.
    /// </summary>
    /// <param name="path">The path to the Movie Set Data Folder.</param>
    /// <returns>A detection result containing the success status, detected naming convention, and descriptive message.</returns>
    [HttpGet("DetectNaming")]
    public ActionResult<NamingDetectionResult> DetectNaming([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(new NamingDetectionResult
            {
                Success = false,
                Message = "Path is empty."
            });
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Ok(new NamingDetectionResult
                {
                    Success = false,
                    Message = "Directory does not exist or is not accessible."
                });
            }

            // 1. Check for flat files directly in the root
            var rootNfoFiles = Directory.EnumerateFiles(path, "*.nfo", SearchOption.TopDirectoryOnly).ToList();
            if (rootNfoFiles.Count > 0)
            {
                return Ok(new NamingDetectionResult
                {
                    Success = true,
                    DetectedConvention = "FlatFile",
                    Message = $"Detected layout: Flat File. Found {rootNfoFiles.Count} NFO file(s) in root."
                });
            }

            // 2. Scan subdirectories (up to a limit to prevent performance issues)
            var subdirs = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Take(30)
                .ToList();

            if (subdirs.Count == 0)
            {
                return Ok(new NamingDetectionResult
                {
                    Success = false,
                    Message = "No files or subdirectories found in the specified path."
                });
            }

            int setSubfolderCount = 0;
            int collectionNfoCount = 0;

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);
                if (string.IsNullOrEmpty(dirName)) continue;

                // Check for collection.nfo
                var collectionNfoPath = Path.Combine(subdir, "collection.nfo");
                if (System.IO.File.Exists(collectionNfoPath))
                {
                    collectionNfoCount++;
                    continue;
                }

                // Check for <subdir>.nfo (allowing case-insensitive matching or fuzzy matching)
                var possibleNfoPath = Path.Combine(subdir, $"{dirName}.nfo");
                if (System.IO.File.Exists(possibleNfoPath))
                {
                    setSubfolderCount++;
                    continue;
                }

                // Fallback: check if there's any other .nfo file in the subdirectory
                try
                {
                    var files = Directory.EnumerateFiles(subdir, "*.nfo", SearchOption.TopDirectoryOnly).ToList();
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        if (!string.Equals(fileName, "collection.nfo", StringComparison.OrdinalIgnoreCase))
                        {
                            setSubfolderCount++;
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore access errors on specific subfolders
                }
            }

            if (collectionNfoCount > 0 && collectionNfoCount >= setSubfolderCount)
            {
                return Ok(new NamingDetectionResult
                {
                    Success = true,
                    DetectedConvention = "CollectionNfo",
                    Message = $"Detected layout: collection.nfo. Found {collectionNfoCount} subfolder(s) containing collection.nfo."
                });
            }

            if (setSubfolderCount > 0)
            {
                return Ok(new NamingDetectionResult
                {
                    Success = true,
                    DetectedConvention = "SetSubfolder",
                    Message = $"Detected layout: Set Subfolder. Found {setSubfolderCount} subfolder(s) containing <SetName>.nfo."
                });
            }

            return Ok(new NamingDetectionResult
            {
                Success = false,
                Message = "Could not identify any supported movie set NFO pattern. Ensure NFO files are generated in this directory (e.g. via tinyMediaManager or MediaElch)."
            });
        }
        catch (Exception ex)
        {
            return Ok(new NamingDetectionResult
            {
                Success = false,
                Message = $"Error scanning folder: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Deletes all BoxSet collections and triggers a fresh scan/sync.
    /// The rebuild runs in the background; this endpoint returns immediately
    /// so the request cannot time out on large libraries.
    /// </summary>
    /// <returns>A response indicating whether the rebuild was started.</returns>
    [HttpPost("ForceRebuild")]
    public IActionResult ForceRebuild()
    {
        if (_manager.IsSyncRunning)
        {
            return BadRequest(new { Success = false, Message = "Sync is already in progress. Please wait for it to complete." });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _manager.ForceRebuildAsync(CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Force rebuild finished");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Force rebuild failed");
            }
        });

        return Ok(new { Success = true, Message = "Force rebuild started. Check the server log for progress." });
    }

    /// <summary>
    /// Validates whether a folder path exists and is readable by the Jellyfin process.
    /// Particularly helpful for Docker/NAS setups with volume mapping or permission issues.
    /// </summary>
    /// <param name="path">The directory path to validate.</param>
    /// <returns>Validation status with diagnostic details.</returns>
    [HttpGet("ValidatePath")]
    public ActionResult<PathValidationResult> ValidatePath([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(new PathValidationResult
            {
                IsValid = false,
                Exists = false,
                IsReadable = false,
                Message = "No path specified."
            });
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Ok(new PathValidationResult
                {
                    IsValid = false,
                    Exists = false,
                    IsReadable = false,
                    Message = $"Path not found: '{path}'. If running in Docker, verify volume mounts and host paths."
                });
            }

            // Test read access
            _ = Directory.EnumerateFileSystemEntries(path).FirstOrDefault();

            return Ok(new PathValidationResult
            {
                IsValid = true,
                Exists = true,
                IsReadable = true,
                Message = "Path is accessible and readable."
            });
        }
        catch (UnauthorizedAccessException uex)
        {
            _logger.LogWarning(uex, "Access denied to path '{Path}'", path);
            return Ok(new PathValidationResult
            {
                IsValid = false,
                Exists = true,
                IsReadable = false,
                Message = $"Access denied to '{path}'. If running in Docker, check PUID/PGID and container permissions."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate path '{Path}'", path);
            return Ok(new PathValidationResult
            {
                IsValid = false,
                Exists = false,
                IsReadable = false,
                Message = $"Unable to read '{path}': {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Returns sanitized, privacy-safe system diagnostic information for GitHub bug reports.
    /// Strictly excludes any personal data, file paths, usernames, IP addresses, or movie titles.
    /// </summary>
    /// <returns>Sanitized system diagnostic information.</returns>
    [HttpGet("SystemInfo")]
    public ActionResult<SystemInfoDto> GetSystemInfo()
    {
        var config = Plugin.Instance?.Configuration;
        var status = _manager.GetStatusSnapshot();

        return Ok(new SystemInfoDto
        {
            PluginVersion = Plugin.Instance?.Version.ToString() ?? "Unknown",
            ServerVersion = _applicationHost.ApplicationVersionString,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            NfoNamingConvention = config?.NfoNaming.ToString() ?? "Not Set",
            MinimumMovies = config?.MinimumMovies ?? 1,
            MountGuardEnabled = config?.EnableMountGuard ?? true,
            MovieFolderFallbackEnabled = config?.EnableMovieFolderArtworkFallback ?? true,
            DeleteOrphanedSets = config?.DeleteOrphanedSets ?? false,
            AggregateRatings = config?.AggregateRatings ?? false,
            AggregateTags = config?.AggregateTags ?? false,
            AggregatePeople = config?.AggregatePeople ?? false,
            AggregateGenres = config?.AggregateGenres ?? false,
            AggregateStudios = config?.AggregateStudios ?? false,
            MaxTags = config?.MaxTags ?? 15,
            MaxActors = config?.MaxActors ?? 20,
            MaxDirectors = config?.MaxDirectors ?? 0,
            MaxWriters = config?.MaxWriters ?? 0,
            MaxGenres = config?.MaxGenres ?? 0,
            MaxStudios = config?.MaxStudios ?? 0,
            CollectionSortBy = config?.CollectionSortBy ?? "Default",
            LastRunOutcome = status.LastRunOutcome,
            ScannedMoviesCount = status.MoviesScanned,
            MoviesInSetsCount = status.MoviesInSets,
            SetsFoundCount = status.SetsFound,
            CollectionsCreatedCount = status.CollectionsCreated,
            CollectionsUpdatedCount = status.CollectionsUpdated,
            CollectionsDeletedCount = status.CollectionsDeleted,
            NfoParseErrorsCount = status.NfoParseErrors,
            LastErrorMessage = status.LastErrorMessage,
            DurationSeconds = status.DurationSeconds,
            RecentLogs = status.RecentLogs,
            HasErrorMessage = !string.IsNullOrEmpty(status.LastErrorMessage)
        });
    }
}

/// <summary>
/// Sanitized, privacy-safe system diagnostic information for bug reporting.
/// Contains strictly zero telemetry, zero paths, zero usernames, and zero item titles.
/// </summary>
public class SystemInfoDto
{
    /// <summary>Gets or sets the plugin version.</summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin server version.</summary>
    public string ServerVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the operating system description.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Gets or sets the OS architecture.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Gets or sets the .NET runtime version.</summary>
    public string DotNetVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the configured NFO naming convention.</summary>
    public string NfoNamingConvention { get; set; } = string.Empty;

    /// <summary>Gets or sets the minimum movies threshold.</summary>
    public int MinimumMovies { get; set; }

    /// <summary>Gets or sets a value indicating whether Mount Guard is enabled.</summary>
    public bool MountGuardEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether movie folder artwork fallback is enabled.</summary>
    public bool MovieFolderFallbackEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether orphaned sets deletion is enabled.</summary>
    public bool DeleteOrphanedSets { get; set; }

    /// <summary>Gets or sets a value indicating whether community ratings aggregation is enabled.</summary>
    public bool AggregateRatings { get; set; }

    /// <summary>Gets or sets a value indicating whether tags aggregation is enabled.</summary>
    public bool AggregateTags { get; set; }

    /// <summary>Gets or sets a value indicating whether people aggregation is enabled.</summary>
    public bool AggregatePeople { get; set; }

    /// <summary>Gets or sets a value indicating whether genre aggregation is enabled.</summary>
    public bool AggregateGenres { get; set; }

    /// <summary>Gets or sets a value indicating whether studio aggregation is enabled.</summary>
    public bool AggregateStudios { get; set; }

    /// <summary>Gets or sets the maximum tags limit.</summary>
    public int MaxTags { get; set; }

    /// <summary>Gets or sets the maximum actors limit.</summary>
    public int MaxActors { get; set; }

    /// <summary>Gets or sets the maximum directors limit.</summary>
    public int MaxDirectors { get; set; }

    /// <summary>Gets or sets the maximum writers limit.</summary>
    public int MaxWriters { get; set; }

    /// <summary>Gets or sets the maximum genres limit.</summary>
    public int MaxGenres { get; set; }

    /// <summary>Gets or sets the maximum studios limit.</summary>
    public int MaxStudios { get; set; }

    /// <summary>Gets or sets the collection sort by setting.</summary>
    public string CollectionSortBy { get; set; } = string.Empty;

    /// <summary>Gets or sets the last sync run outcome.</summary>
    public string LastRunOutcome { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of movies scanned.</summary>
    public int ScannedMoviesCount { get; set; }

    /// <summary>Gets or sets the number of movies belonging to sets.</summary>
    public int MoviesInSetsCount { get; set; }

    /// <summary>Gets or sets the number of sets found.</summary>
    public int SetsFoundCount { get; set; }

    /// <summary>Gets or sets the number of collections created in the last sync.</summary>
    public int CollectionsCreatedCount { get; set; }

    /// <summary>Gets or sets the number of collections updated in the last sync.</summary>
    public int CollectionsUpdatedCount { get; set; }

    /// <summary>Gets or sets the number of collections deleted in the last sync.</summary>
    public int CollectionsDeletedCount { get; set; }

    /// <summary>Gets or sets the number of NFO parse errors encountered.</summary>
    public int NfoParseErrorsCount { get; set; }

    /// <summary>Gets or sets the error message of the last sync run, if any.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Gets or sets the duration of the last sync run in seconds.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Gets or sets recent execution logs from the sync manager.</summary>
    public IReadOnlyList<string> RecentLogs { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets a value indicating whether an error message is recorded.</summary>
    public bool HasErrorMessage { get; set; }
}

/// <summary>
/// Data model returned by the path validation API.
/// </summary>
public class PathValidationResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the path exists and is readable.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the directory exists.
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the directory is readable.
    /// </summary>
    public bool IsReadable { get; set; }

    /// <summary>
    /// Gets or sets status or error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Data model returned by the conflict check API.
/// </summary>
public class ConflictCheckResult
{
    /// <summary>
    /// Gets or sets a value indicating whether any library has conflict settings enabled.
    /// </summary>
    public bool HasConflicts { get; set; }

    /// <summary>
    /// Gets or sets the list of library names that have conflicting settings.
    /// </summary>
    public List<string> ConflictingLibraries { get; set; } = [];
}

/// <summary>
/// Data model returned by the naming convention auto-detection API.
/// </summary>
public class NamingDetectionResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the naming convention was successfully detected.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the detected naming convention name.
    /// </summary>
    public string? DetectedConvention { get; set; }

    /// <summary>
    /// Gets or sets status or error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Information about a Jellyfin library.
/// </summary>
public class LibraryInfoDto
{
    /// <summary>Gets or sets the library ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the library name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the library locations.</summary>
    public string[] Locations { get; set; } = [];
}
