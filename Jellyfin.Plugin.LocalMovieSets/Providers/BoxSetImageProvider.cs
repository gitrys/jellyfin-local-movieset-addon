using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Providers;

/// <summary>
/// Supplies collection artwork to Jellyfin's image refresh pipeline.
/// Artwork is looked up first in the dedicated tinyMediaManager set data folder.
/// If not found (or no central folder is configured), it falls back to scanning
/// the individual member movie folders for movie-set artwork (e.g. movieset-poster.jpg).
/// Running inside the pipeline (instead of saving imperatively during sync) means all
/// writes to the item are serialized and cannot race Jellyfin's own collection.xml saves.
/// </summary>
public class BoxSetImageProvider : IDynamicImageProvider
{
    /// <summary>
    /// Provider name as registered with Jellyfin. Must match what shows up in
    /// the library's image fetcher configuration.
    /// </summary>
    public const string ProviderName = "Local Movie Sets";

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly LocalMovieSetManager _setManager;
    private readonly ILogger<BoxSetImageProvider> _logger;

    /// <summary>
    /// Maps central set folder artwork filenames (in priority order) to Jellyfin image types.
    /// </summary>
    internal static readonly IReadOnlyList<(string[] FileNames, ImageType ImageType)> ArtworkMappings =
    [
        (["poster.jpg",    "poster.png",    "folder.jpg",   "folder.png"],  ImageType.Primary),
        (["fanart.jpg",    "fanart.png",    "backdrop.jpg", "backdrop.png"], ImageType.Backdrop),
        (["logo.png",      "logo.jpg",      "clearlogo.png"],               ImageType.Logo),
        (["landscape.jpg", "landscape.png", "thumb.jpg",    "thumb.png"],   ImageType.Thumb),
        (["clearart.png",  "clearart.jpg"],                                 ImageType.Art),
        (["banner.jpg",    "banner.png"],                                   ImageType.Banner),
        (["disc.png",      "disc.jpg",      "discart.png"],                 ImageType.Disc),
        (["box.jpg",       "box.png"],                                      ImageType.Box),
        (["boxrear.jpg",   "boxrear.png",   "box_rear.jpg", "box_rear.png", "back.jpg", "back.png"], ImageType.BoxRear),
        (["menu.jpg",      "menu.png"],                                     ImageType.Menu),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="BoxSetImageProvider"/> class.
    /// </summary>
    /// <param name="setManager">Local movie set manager instance (injected).</param>
    /// <param name="logger">Logger instance (injected).</param>
    public BoxSetImageProvider(
        LocalMovieSetManager setManager,
        ILogger<BoxSetImageProvider> logger)
    {
        _setManager = setManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is BoxSet;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        if (item is not BoxSet boxSet)
        {
            yield break;
        }

        var artworkFolder = GetArtworkFolderForItem(boxSet);

        foreach (var (fileNames, imageType) in ArtworkMappings)
        {
            // Check central folder first
            if (artworkFolder is not null && FindFirstExistingFile(artworkFolder, fileNames) is not null)
            {
                yield return imageType;
                continue;
            }

            // Check movie folders as fallback
            if (FindArtworkInMovieFolders(boxSet, imageType) is not null)
            {
                yield return imageType;
            }
        }
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
    {
        if (item is not BoxSet boxSet)
        {
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        string? imagePath = null;
        var artworkFolder = GetArtworkFolderForItem(boxSet);

        // 1. Try central set folder
        if (artworkFolder is not null)
        {
            foreach (var (fileNames, imageType) in ArtworkMappings)
            {
                if (imageType == type)
                {
                    imagePath = FindFirstExistingFile(artworkFolder, fileNames);
                    break;
                }
            }
        }

        // 2. Fallback to individual movie folders
        if (imagePath is null)
        {
            imagePath = FindArtworkInMovieFolders(boxSet, type);
        }

        if (imagePath is not null)
        {
            _logger.LogInformation(
                "Providing {ImageType} image for collection '{SetName}' from {Path}",
                type, item.Name, imagePath);

            // Return the image as a stream, not a path: Jellyfin deletes a
            // returned path after copying it (it assumes a temp file), which
            // would destroy the user's permanent set artwork on a writable
            // share and spams UnauthorizedAccessException on a read-only one.
            var response = new DynamicImageResponse
            {
                HasImage = true,
                Stream = File.OpenRead(imagePath)
            };
            response.SetFormatFromMimeType(GetMimeType(imagePath));

            return Task.FromResult(response);
        }

        return Task.FromResult(new DynamicImageResponse { HasImage = false });
    }

    /// <summary>
    /// Searches individual movie folders belonging to this box set for matching set artwork.
    /// </summary>
    private string? FindArtworkInMovieFolders(BoxSet boxSet, ImageType type)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is not null && !config.EnableMovieFolderArtworkFallback)
        {
            return null;
        }

        var candidateNames = GetMovieFolderCandidateFileNames(boxSet.Name, type);
        if (candidateNames.Count == 0)
        {
            return null;
        }

        var movies = _setManager.GetBoxSetMovies(boxSet);
        if (movies.Count == 0)
        {
            return null;
        }

        var checkedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var movie in movies)
        {
            var folder = Path.GetDirectoryName(movie.Path);
            if (string.IsNullOrWhiteSpace(folder) || !checkedFolders.Add(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            var match = FindFirstExistingFile(folder, candidateNames);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Generates the list of candidate filenames for set artwork stored in a movie folder,
    /// matching Kodi and tinyMediaManager conventions.
    /// </summary>
    internal static IReadOnlyList<string> GetMovieFolderCandidateFileNames(string setName, ImageType type)
    {
        var baseNames = GetMovieFolderBaseNames(setName, type);
        if (baseNames.Count == 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>(baseNames.Count * ImageExtensions.Length);
        foreach (var baseName in baseNames)
        {
            foreach (var ext in ImageExtensions)
            {
                results.Add(baseName + ext);
            }
        }

        return results;
    }

    private static IReadOnlyList<string> GetMovieFolderBaseNames(string setName, ImageType type)
    {
        var nameVariants = string.IsNullOrWhiteSpace(setName)
            ? []
            : SetNfoParser.GetFolderNameCandidates(setName);

        var list = new List<string>();

        void AddWithVariants(string genericPrefix, string suffix)
        {
            list.Add($"{genericPrefix}-{suffix}");
            foreach (var variant in nameVariants)
            {
                var candidate = $"{variant}-{suffix}";
                if (!list.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(candidate);
                }
            }
        }

        switch (type)
        {
            case ImageType.Primary:
                AddWithVariants("movieset", "poster");
                AddWithVariants("movieset", "folder");
                break;
            case ImageType.Backdrop:
                AddWithVariants("movieset", "fanart");
                AddWithVariants("movieset", "backdrop");
                break;
            case ImageType.Logo:
                AddWithVariants("movieset", "clearlogo");
                AddWithVariants("movieset", "logo");
                break;
            case ImageType.Thumb:
                AddWithVariants("movieset", "thumb");
                AddWithVariants("movieset", "landscape");
                break;
            case ImageType.Art:
                AddWithVariants("movieset", "clearart");
                break;
            case ImageType.Banner:
                AddWithVariants("movieset", "banner");
                break;
            case ImageType.Disc:
                AddWithVariants("movieset", "disc");
                AddWithVariants("movieset", "discart");
                break;
        }

        return list;
    }

    /// <summary>
    /// Resolves the set artwork folder for a BoxSet item, or <c>null</c> when
    /// no set data folder is configured or the folder does not exist.
    /// </summary>
    private static string? GetArtworkFolderForItem(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.SetDataFolder))
        {
            return null;
        }

        return SetNfoParser.ResolveArtworkFolder(config.SetDataFolder, item.Name, config.NfoNaming);
    }

    internal static string? FindFirstExistingFile(string folder, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(folder, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Returns the MIME type for a given image file path.</summary>
    private static string GetMimeType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"             => "image/png",
            ".webp"            => "image/webp",
            ".gif"             => "image/gif",
            _                  => "image/jpeg"
        };
}
