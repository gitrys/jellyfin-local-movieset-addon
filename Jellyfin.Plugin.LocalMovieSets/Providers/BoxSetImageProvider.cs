using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Providers;

/// <summary>
/// Supplies collection artwork from the tinyMediaManager set data folder to
/// Jellyfin's image refresh pipeline. The pipeline copies each returned file
/// into the collection's own folder, so Jellyfin owns its copy and an offline
/// share cannot strip images. Running inside the pipeline (instead of saving
/// imperatively during sync) means all writes to the item are serialized and
/// cannot race Jellyfin's own collection.xml saves.
/// </summary>
public class BoxSetImageProvider : IDynamicImageProvider
{
    /// <summary>
    /// Provider name as registered with Jellyfin. Must match what shows up in
    /// the library's image fetcher configuration.
    /// </summary>
    public const string ProviderName = "Local Movie Sets";

    private readonly ILogger<BoxSetImageProvider> _logger;

    /// <summary>
    /// Maps artwork filenames (in priority order) to Jellyfin image types.
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
    /// <param name="logger">Logger instance (injected).</param>
    public BoxSetImageProvider(ILogger<BoxSetImageProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is BoxSet;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        var artworkFolder = GetArtworkFolderForItem(item);
        if (artworkFolder is null)
        {
            yield break;
        }

        foreach (var (fileNames, imageType) in ArtworkMappings)
        {
            if (FindFirstExistingFile(artworkFolder, fileNames) is not null)
            {
                yield return imageType;
            }
        }
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
    {
        var artworkFolder = GetArtworkFolderForItem(item);
        if (artworkFolder is null)
        {
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        foreach (var (fileNames, imageType) in ArtworkMappings)
        {
            if (imageType != type)
            {
                continue;
            }

            var imagePath = FindFirstExistingFile(artworkFolder, fileNames);
            if (imagePath is null)
            {
                break;
            }

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
    /// Resolves the set artwork folder for a BoxSet item, or <c>null</c> when
    /// no set data folder is configured or the folder does not exist.
    /// </summary>
    private static string? GetArtworkFolderForItem(BaseItem item)
    {
        // Jellyfin probes providers with a nameless dummy item when building
        // the library options UI (/Libraries/AvailableOptions); a null name
        // must not throw or the whole endpoint 500s.
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

    private static string? FindFirstExistingFile(string folder, string[] fileNames)
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
