using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Parsers;

/// <summary>
/// Metadata read from a dedicated set .nfo file (the "Movie Set Data Folder" in TMM).
/// </summary>
/// <param name="Title">The display title of the collection.</param>
/// <param name="OriginalTitle">Optional original/alternate title.</param>
/// <param name="Overview">Optional description of the collection.</param>
/// <param name="Genres">List of parsed genres.</param>
/// <param name="Studios">List of parsed studios.</param>
/// <param name="TmdbId">Optional TMDB collection ID.</param>
/// <param name="ImdbId">Optional IMDb collection ID.</param>
public record SetNfoInfo(
    string Title,
    string? OriginalTitle,
    string? Overview,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Studios,
    string? TmdbId,
    string? ImdbId);


/// <summary>
/// Parses a tinyMediaManager dedicated set .nfo file from the configured
/// Movie Set Data Folder. Supports all three TMM naming conventions.
/// </summary>
public class SetNfoParser
{
    private readonly ILogger<SetNfoParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetNfoParser"/> class.
    /// </summary>
    /// <param name="logger">Logger instance (injected).</param>
    public SetNfoParser(ILogger<SetNfoParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to read and parse the dedicated NFO file for a given set name
    /// from the configured set data folder.
    /// </summary>
    /// <param name="setDataFolder">Path to the TMM Movie Set Data Folder.</param>
    /// <param name="setName">Name of the movie set.</param>
    /// <param name="naming">NFO file naming convention configured in plugin settings.</param>
    /// <returns>Parsed <see cref="SetNfoInfo"/>, or <c>null</c> if not found or invalid.</returns>
    public SetNfoInfo? ParseSet(string setDataFolder, string setName, NfoNamingConvention naming)
    {
        if (string.IsNullOrWhiteSpace(setDataFolder))
            return null;

        if (!Directory.Exists(setDataFolder))
        {
            _logger.LogWarning("Set data folder does not exist: {Folder}", setDataFolder);
            return null;
        }

        var nfoPath = GetNfoPath(setDataFolder, setName, naming);

        if (!File.Exists(nfoPath))
        {
            _logger.LogDebug("No set NFO found at: {NfoPath}", nfoPath);
            return null;
        }

        _logger.LogDebug("Parsing set NFO: {NfoPath}", nfoPath);

        try
        {
            var doc = NfoXmlLoader.Load(nfoPath);
            var root = doc.Root;
            if (root is null)
                return null;

            var title = root.Element("title")?.Value?.Trim();
            var originalTitle = root.Element("originaltitle")?.Value?.Trim();
            var overview = root.Element("plot")?.Value?.Trim();
            if (string.IsNullOrEmpty(overview))
            {
                overview = root.Element("overview")?.Value?.Trim();
            }

            var genres = root.Elements("genre")
                .Select(e => e.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList();

            var studios = root.Elements("studio")
                .Select(e => e.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList();

            // Parse TMDB and IMDb IDs
            var tmdbId = root.Element("tmdbid")?.Value?.Trim();
            if (string.IsNullOrEmpty(tmdbId))
            {
                tmdbId = root.Elements("uniqueid")
                    .FirstOrDefault(x => string.Equals(x.Attribute("type")?.Value?.Trim(), "tmdb", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();
            }

            var imdbId = root.Element("imdbid")?.Value?.Trim();
            if (string.IsNullOrEmpty(imdbId))
            {
                imdbId = root.Elements("uniqueid")
                    .FirstOrDefault(x => string.Equals(x.Attribute("type")?.Value?.Trim(), "imdb", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();
            }

            // Fall back to set name if no title in file
            if (string.IsNullOrWhiteSpace(title))
                title = setName;

            return new SetNfoInfo(
                title,
                string.IsNullOrEmpty(originalTitle) ? null : originalTitle,
                string.IsNullOrEmpty(overview) ? null : overview,
                genres,
                studios,
                string.IsNullOrEmpty(tmdbId) ? null : tmdbId,
                string.IsNullOrEmpty(imdbId) ? null : imdbId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse set NFO at {NfoPath}", nfoPath);
            return null;
        }
    }

    /// <summary>
    /// Resolves the expected NFO file path based on the naming convention.
    /// </summary>
    /// <param name="setDataFolder">Root set data folder.</param>
    /// <param name="setName">Set name.</param>
    /// <param name="naming">Naming convention.</param>
    /// <returns>Full path to the expected NFO file.</returns>
    public static string GetNfoPath(string setDataFolder, string setName, NfoNamingConvention naming)
    {
        var safeName = SanitizeFolderName(setName);
        return naming switch
        {
            NfoNamingConvention.SetSubfolder  => Path.Combine(setDataFolder, safeName, $"{safeName}.nfo"),
            NfoNamingConvention.FlatFile       => Path.Combine(setDataFolder, $"{safeName}.nfo"),
            NfoNamingConvention.CollectionNfo  => Path.Combine(setDataFolder, safeName, "collection.nfo"),
            _                                  => Path.Combine(setDataFolder, safeName, $"{safeName}.nfo")
        };
    }

    /// <summary>
    /// Resolves the directory that contains the set's artwork files.
    /// For flat-file naming the artwork sits directly in the set data folder.
    /// For all subfolder styles the artwork sits in the set's named subfolder.
    /// </summary>
    /// <param name="setDataFolder">Root set data folder.</param>
    /// <param name="setName">Set name.</param>
    /// <param name="naming">Naming convention.</param>
    /// <returns>Full path to the directory containing artwork files.</returns>
    public static string GetArtworkFolder(string setDataFolder, string setName, NfoNamingConvention naming)
    {
        if (naming == NfoNamingConvention.FlatFile)
            return setDataFolder;

        var safeName = SanitizeFolderName(setName);
        return Path.Combine(setDataFolder, safeName);
    }

    /// <summary>
    /// Characters invalid in file/folder names on Windows or Unix paths.
    /// Using a fixed set (not <see cref="Path.GetInvalidFileNameChars"/> alone)
    /// keeps sanitization consistent across OSes — Jellyfin often runs on Linux
    /// while set names may originate from Windows-oriented tools like TMM.
    /// </summary>
    private static readonly char[] CrossPlatformInvalidFileNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0'];

    /// <summary>
    /// Strips characters that are invalid in file/folder names so the set name
    /// can be used as a filesystem path component.
    /// </summary>
    /// <param name="name">Raw set name.</param>
    /// <returns>Sanitized name safe for use as a folder/file name.</returns>
    public static string SanitizeFolderName(string name)
    {
        var invalid = CrossPlatformInvalidFileNameChars;
        var sanitized = string.Join(string.Empty, name.Split(invalid));

        // Guard against "." / ".." which Path.Combine would resolve as current/parent dir
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized.All(c => c == '.'))
        {
            return "_" + sanitized;
        }

        return sanitized;
    }
}
