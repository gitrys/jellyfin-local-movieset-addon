using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalMovieSetsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager (injected).</param>
    public LocalMovieSetsController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
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
