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
/// Result of parsing a movie's NFO file(s).
/// </summary>
/// <param name="Membership">The set membership, or <c>null</c> if the movie is not in a set.</param>
/// <param name="HadError">
/// <c>true</c> when an NFO file existed but could not be parsed and no membership
/// was obtained from any other candidate file.
/// </param>
public record NfoParseOutcome(SetMembership? Membership, bool HadError);

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
    /// returning the set membership if a &lt;set&gt;&lt;name&gt; tag is found,
    /// and whether any NFO file failed to parse.
    /// </summary>
    /// <param name="movieFilePath">Full path to the movie video file (e.g. movie.mkv).</param>
    /// <returns>An <see cref="NfoParseOutcome"/> with the membership (if any) and error flag.</returns>
    public NfoParseOutcome ParseNfo(string movieFilePath)
    {
        var hadError = false;

        foreach (var nfoPath in GetNfoCandidates(movieFilePath))
        {
            if (!File.Exists(nfoPath))
                continue;

            _logger.LogDebug("Parsing movie NFO: {NfoPath}", nfoPath);

            try
            {
                var doc = NfoXmlLoader.Load(nfoPath);
                var root = doc.Root;

                // Support both <movie> root (standard) and bare root elements
                if (root is null)
                    continue;

                var setElement = root.Element("set");
                if (setElement is null)
                    return new NfoParseOutcome(null, false);  // NFO exists but no <set> tag → movie is not in a set

                var setName = setElement.Element("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(setName))
                {
                    if (!setElement.HasElements)
                    {
                        setName = setElement.Value?.Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(setName))
                    return new NfoParseOutcome(null, false);

                var setOverview = setElement.Element("overview")?.Value?.Trim();

                _logger.LogDebug("Movie '{MoviePath}' belongs to set '{SetName}'", movieFilePath, setName);
                var membership = new SetMembership(setName, string.IsNullOrEmpty(setOverview) ? null : setOverview);
                return new NfoParseOutcome(membership, false);
            }
            catch (Exception ex)
            {
                hadError = true;
                _logger.LogWarning(ex, "Failed to parse NFO file: {NfoPath}", nfoPath);
            }
        }

        return new NfoParseOutcome(null, hadError);
    }

    /// <summary>
    /// Returns the candidate NFO file paths for a given movie path, in priority order.
    /// Kodi/TMM convention: {MovieName}.nfo takes priority, then movie.nfo.
    /// For folder-based rips (DVD/BD structures) the item path is a directory
    /// and the NFO lives inside that directory.
    /// </summary>
    private static IEnumerable<string> GetNfoCandidates(string movieFilePath)
    {
        if (Directory.Exists(movieFilePath))
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(movieFilePath));

            // 1. {FolderName}.nfo inside the movie folder
            if (!string.IsNullOrEmpty(folderName))
                yield return Path.Combine(movieFilePath, $"{folderName}.nfo");

            // 2. movie.nfo inside the movie folder
            yield return Path.Combine(movieFilePath, "movie.nfo");
            yield break;
        }

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
