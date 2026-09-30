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
    /// <param name="imageFetchers">Enabled image fetcher names. Ignored when <paramref name="hasBoxSetTypeOptions"/> is <c>false</c>.</param>
    /// <returns>A result with <see cref="ImageFetcherCheckResult.HasConflict"/> set when the list is not local-only.</returns>
    public static ImageFetcherCheckResult Evaluate(bool hasBoxSetTypeOptions, IEnumerable<string>? imageFetchers)
    {
        if (!hasBoxSetTypeOptions)
        {
            return new ImageFetcherCheckResult
            {
                HasConflict = true,
                Reason = NoBoxSetOptions
            };
        }

        var names = (imageFetchers ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var onlyLocal = names.Count == 1
            && string.Equals(names[0], BoxSetImageProvider.ProviderName, StringComparison.OrdinalIgnoreCase);

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
}
