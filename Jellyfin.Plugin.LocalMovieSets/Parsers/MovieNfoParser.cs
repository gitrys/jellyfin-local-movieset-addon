using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Parsers;

/// <summary>
/// Represents the movie set a movie belongs to, as declared in its .nfo file.
/// </summary>
/// <param name="SetName">The name of the set (collection).</param>
/// <param name="SetOverview">Optional overview/description of the set from the movie's NFO.</param>
public record SetMembership(string SetName, string? SetOverview);

/// <summary>
/// Parses a Kodi/tinyMediaManager style movie .nfo file and extracts the
/// &lt;set&gt;&lt;name&gt; tag that identifies which collection a movie belongs to.
/// </summary>
public class MovieNfoParser
{
    private readonly ILogger<MovieNfoParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieNfoParser"/> class.
    /// </summary>
    /// <param name="logger">Logger instance (injected).</param>
    public MovieNfoParser(ILogger<MovieNfoParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to read and parse the NFO file for the given movie file path,
    /// returning the set membership if a &lt;set&gt;&lt;name&gt; tag is found.
    /// </summary>
    /// <param name="movieFilePath">Full path to the movie video file (e.g. movie.mkv).</param>
    /// <returns>A <see cref="SetMembership"/> if the NFO contains a set tag; otherwise <c>null</c>.</returns>
    public SetMembership? ParseNfo(string movieFilePath)
    {
        foreach (var nfoPath in GetNfoCandidates(movieFilePath))
        {
            if (!File.Exists(nfoPath))
                continue;

            _logger.LogDebug("Parsing movie NFO: {NfoPath}", nfoPath);

            try
            {
                var doc = XDocument.Load(nfoPath, LoadOptions.None);
                var root = doc.Root;

                // Support both <movie> root (standard) and bare root elements
                if (root is null)
                    continue;

                var setElement = root.Element("set");
                if (setElement is null)
                    return null;  // NFO exists but no <set> tag → movie is not in a set

                var setName = setElement.Element("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(setName))
                {
                    if (!setElement.HasElements)
                    {
                        setName = setElement.Value?.Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(setName))
                    return null;

                var setOverview = setElement.Element("overview")?.Value?.Trim();

                _logger.LogDebug("Movie '{MoviePath}' belongs to set '{SetName}'", movieFilePath, setName);
                return new SetMembership(setName, string.IsNullOrEmpty(setOverview) ? null : setOverview);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse NFO file: {NfoPath}", nfoPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the candidate NFO file paths for a given movie file, in priority order.
    /// Kodi/TMM convention: {MovieName}.nfo takes priority, then movie.nfo.
    /// </summary>
    private static IEnumerable<string> GetNfoCandidates(string movieFilePath)
    {
        var dir = Path.GetDirectoryName(movieFilePath);
        if (string.IsNullOrEmpty(dir))
            yield break;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(movieFilePath);

        // 1. {MovieFileName}.nfo — same name as the video file
        yield return Path.Combine(dir, $"{nameWithoutExt}.nfo");

        // 2. movie.nfo — generic fallback name
        yield return Path.Combine(dir, "movie.nfo");
    }
}
