using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Copies artwork files from a tinyMediaManager set folder into a Jellyfin
/// BoxSet collection item using <see cref="IProviderManager"/>.
/// </summary>
public class SetArtworkProvider
{
    private readonly IProviderManager _providerManager;
    private readonly ILogger<SetArtworkProvider> _logger;

    /// <summary>
    /// Maps artwork filenames (in priority order) to Jellyfin image types.
    /// </summary>
    private static readonly IReadOnlyList<(string[] FileNames, ImageType ImageType)> ArtworkMappings =
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
    /// Initializes a new instance of the <see cref="SetArtworkProvider"/> class.
    /// </summary>
    /// <param name="providerManager">Jellyfin provider manager (injected).</param>
    /// <param name="logger">Logger instance (injected).</param>
    public SetArtworkProvider(IProviderManager providerManager, ILogger<SetArtworkProvider> logger)
    {
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>
    /// Scans the TMM set artwork folder and saves any found images to the given
    /// Jellyfin collection item.
    /// </summary>
    /// <param name="item">The Jellyfin BoxSet item to apply artwork to.</param>
    /// <param name="setDataFolder">Path to the TMM Movie Set Data Folder.</param>
    /// <param name="setName">Name of the movie set.</param>
    /// <param name="naming">NFO naming convention (determines artwork folder path).</param>
    /// <param name="overwrite">
    /// When <c>true</c>, existing artwork is overwritten.
    /// When <c>false</c>, image types already present on the item are skipped.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncArtworkAsync(
        BaseItem item,
        string setDataFolder,
        string setName,
        NfoNamingConvention naming,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(setDataFolder))
            return;

        var artworkFolder = SetNfoParser.GetArtworkFolder(setDataFolder, setName, naming);
        if (!Directory.Exists(artworkFolder))
        {
            _logger.LogDebug("No artwork folder found for set '{SetName}' at {Folder}", setName, artworkFolder);
            return;
        }

        foreach (var (fileNames, imageType) in ArtworkMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip if image already exists and overwrite is disabled
            if (!overwrite && item.HasImage(imageType, 0))
                continue;

            foreach (var fileName in fileNames)
            {
                var imagePath = Path.Combine(artworkFolder, fileName);
                if (!File.Exists(imagePath))
                    continue;

                try
                {
                    var mimeType = GetMimeType(imagePath);
                    _logger.LogInformation(
                        "Saving {ImageType} image for collection '{SetName}' from {Path}",
                        imageType, setName, imagePath);

                    await using var stream = File.OpenRead(imagePath);
                    await _providerManager
                        .SaveImage(item, stream, mimeType, imageType, null, cancellationToken)
                        .ConfigureAwait(false);

                    break; // Only use the first found file for each image type
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to save {ImageType} image for set '{SetName}' from {Path}",
                        imageType, setName, imagePath);
                }
            }
        }
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
