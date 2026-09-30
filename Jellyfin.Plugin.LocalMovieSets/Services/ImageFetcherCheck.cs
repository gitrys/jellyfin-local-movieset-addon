using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalMovieSets.Providers;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Decides whether a collections library's BoxSet image fetchers are limited
/// to this plugin. A missing per-type list is a conflict: Jellyfin then uses
/// the server-wide defaults, where online fetchers stay enabled.
/// </summary>
internal static class ImageFetcherCheck
{
    /// <summary>No saved BoxSet image-fetcher list. Server defaults apply.</summary>
    public const string NoBoxSetOptions = "NoBoxSetOptions";

    /// <summary>The saved list is not exactly this plugin's image fetcher.</summary>
    public const string NotOnlyLocalMovieSets = "NotOnlyLocalMovieSets";

    /// <summary>Jellyfin has no collections library to inspect.</summary>
    public const string NoCollectionsLibrary = "NoCollectionsLibrary";

    /// <summary>
    /// Returns whether the saved BoxSet image fetchers are exactly <c>Local Movie Sets</c>.
    /// </summary>
    /// <param name="hasBoxSetTypeOptions"><c>false</c> when the library has no BoxSet type options.</param>
    /// <param name="fetchers">Enabled fetcher names. Ignored when <paramref name="hasBoxSetTypeOptions"/> is <c>false</c>.</param>
    /// <param name="expectedName">The single allowed provider name. Defaults to the image provider.</param>
    /// <returns>A result with <see cref="ImageFetcherCheckResult.HasConflict"/> set when the list is not local-only.</returns>
    public static ImageFetcherCheckResult Evaluate(
        bool hasBoxSetTypeOptions,
        IEnumerable<string>? fetchers,
        string? expectedName = null)
    {
        if (!hasBoxSetTypeOptions)
        {
            return new ImageFetcherCheckResult
            {
                HasConflict = true,
                Reason = NoBoxSetOptions
            };
        }

        var allowedName = string.IsNullOrWhiteSpace(expectedName)
            ? BoxSetImageProvider.ProviderName
            : expectedName;

        var names = (fetchers ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var onlyLocal = names.Count == 1
            && string.Equals(names[0], allowedName, StringComparison.OrdinalIgnoreCase);

        if (onlyLocal)
        {
            return new ImageFetcherCheckResult { HasConflict = false };
        }

        return new ImageFetcherCheckResult
        {
            HasConflict = true,
            Reason = NotOnlyLocalMovieSets,
            EnabledFetchers = names
        };
    }

    /// <summary>
    /// Returns whether any internet metadata downloader is enabled for BoxSets.
    /// An empty list is fine: this plugin is a custom provider and does not appear in that list.
    /// </summary>
    /// <param name="hasBoxSetTypeOptions"><c>false</c> when the library has no BoxSet type options.</param>
    /// <param name="metadataDownloaders">Enabled metadata downloader names.</param>
    /// <returns>A conflict when a downloader other than this plugin is enabled, or when no BoxSet list is saved.</returns>
    public static ImageFetcherCheckResult EvaluateMetadataDownloaders(
        bool hasBoxSetTypeOptions,
        IEnumerable<string>? metadataDownloaders)
    {
        if (!hasBoxSetTypeOptions)
        {
            return new ImageFetcherCheckResult
            {
                HasConflict = true,
                Reason = NoBoxSetOptions
            };
        }

        var others = (metadataDownloaders ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !string.Equals(name, BoxSetMetadataProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (others.Count == 0)
        {
            return new ImageFetcherCheckResult { HasConflict = false };
        }

        return new ImageFetcherCheckResult
        {
            HasConflict = true,
            Reason = NotOnlyLocalMovieSets,
            EnabledFetchers = others
        };
    }

    /// <summary>
    /// Merges the image-fetcher and metadata-downloader checks into one banner result.
    /// </summary>
    /// <param name="images">Image fetcher check.</param>
    /// <param name="metadata">Metadata downloader check. Its fetcher names are copied to <see cref="ImageFetcherCheckResult.EnabledMetadataFetchers"/>.</param>
    /// <returns>A conflict when either list is not local-only.</returns>
    public static ImageFetcherCheckResult Combine(ImageFetcherCheckResult images, ImageFetcherCheckResult metadata)
    {
        return new ImageFetcherCheckResult
        {
            HasConflict = images.HasConflict || metadata.HasConflict,
            Reason = images.Reason,
            EnabledFetchers = images.EnabledFetchers,
            MetadataReason = metadata.Reason,
            EnabledMetadataFetchers = metadata.EnabledFetchers
        };
    }
}

/// <summary>
/// Result of the collections image-fetcher check.
/// </summary>
public class ImageFetcherCheckResult
{
    /// <summary>Gets or sets a value indicating whether the fetcher list is not local-only.</summary>
    public bool HasConflict { get; set; }

    /// <summary>Gets or sets why the check failed. Empty when there is no conflict.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets the collections library name, when one was inspected.</summary>
    public string? LibraryName { get; set; }

    /// <summary>Gets or sets the enabled image fetcher names from the saved list.</summary>
    public List<string> EnabledFetchers { get; set; } = [];

    /// <summary>Gets or sets why the metadata-downloader check failed. Empty when that list is local-only.</summary>
    public string MetadataReason { get; set; } = string.Empty;

    /// <summary>Gets or sets the enabled metadata downloader names from the saved list.</summary>
    public List<string> EnabledMetadataFetchers { get; set; } = [];
}
