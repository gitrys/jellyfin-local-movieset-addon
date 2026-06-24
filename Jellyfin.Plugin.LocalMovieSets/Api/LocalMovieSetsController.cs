using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager (injected).</param>
    /// <param name="manager">LocalMovieSetManager instance (injected).</param>
    public LocalMovieSetsController(ILibraryManager libraryManager, LocalMovieSetManager manager)
    {
        _libraryManager = libraryManager;
        _manager = manager;
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
    /// </summary>
    /// <returns>A response indicating success or failure.</returns>
    [HttpPost("ForceRebuild")]
    public async Task<IActionResult> ForceRebuild(CancellationToken cancellationToken)
    {
        try
        {
            await _manager.ForceRebuildAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new { Success = true, Message = "Force rebuild started successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Message = ex.Message });
        }
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
