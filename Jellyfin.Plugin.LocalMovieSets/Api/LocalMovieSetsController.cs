using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Library;
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
    private readonly ILogger<LocalMovieSetsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager (injected).</param>
    /// <param name="manager">LocalMovieSetManager instance (injected).</param>
    /// <param name="logger">Logger instance (injected).</param>
    public LocalMovieSetsController(
        ILibraryManager libraryManager,
        LocalMovieSetManager manager,
        ILogger<LocalMovieSetsController> logger)
    {
        _libraryManager = libraryManager;
        _manager = manager;
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
                Message = "Could not identify any tinyMediaManager NFO pattern. Ensure NFOs are generated in this directory."
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
