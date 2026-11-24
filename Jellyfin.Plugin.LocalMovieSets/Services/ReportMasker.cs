using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LocalMovieSets.Services;

/// <summary>
/// Paths and names the server knows, used to mask a bug report.
/// </summary>
/// <param name="LibraryRoots">Library folder paths, replaced by "[Library n]".</param>
/// <param name="SetDataFolder">Movie Set Data Folder, replaced by "[Set Data Folder]".</param>
/// <param name="SetNames">Set and collection names, matched case-insensitively.</param>
/// <param name="MovieNames">Movie titles, matched case-sensitively and only from <see cref="ReportMasker.MinMovieNameLength"/> characters.</param>
public record ReportMaskTerms(
    IReadOnlyCollection<string> LibraryRoots,
    string? SetDataFolder,
    IReadOnlyCollection<string> SetNames,
    IReadOnlyCollection<string> MovieNames);

/// <summary>
/// Masks file paths and set/movie names in the bug report text. Paths are found by
/// shape (Windows drive, UNC, POSIX; folder names may contain spaces and non-ASCII
/// characters); names come from the server, so titles are masked whatever they look
/// like. The same path root or name always gets the same placeholder number.
/// </summary>
public static class ReportMasker
{
    /// <summary>Movie titles shorter than this are not masked (they match too many ordinary words).</summary>
    public const int MinMovieNameLength = 3;

    private const string SetDataFolderPlaceholder = "[Set Data Folder]";
    private const string Ellipsis = "…";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    // A path ends at a quote, backtick, angle bracket, pipe or line break. Segments
    // may contain spaces, so the last segment can run to the end of the sentence; it
    // is kept as written, so only the folders in front of it disappear.
    private const string SegmentChars = @"[^\\/'""`<>|\r\n]";

    private static readonly Regex PathRegex = new(
        @"(?:"
        + @"\\\\" + SegmentChars + @"+(?:[\\/]" + SegmentChars + @"*)*" // UNC: \\server\share\...
        + @"|(?<![\p{L}\p{N}])[A-Za-z]:[\\/]" + SegmentChars + @"*(?:[\\/]" + SegmentChars + @"*)*" // C:\... or C:/...
        + @"|(?<=^|[\s'""`(\[=:,])/[^\s\\/'""`<>|]" + SegmentChars + @"*(?:/" + SegmentChars + @"*)*" // /a/b
        + @")",
        RegexOptions.CultureInvariant | RegexOptions.Multiline,
        MatchTimeout);

    // Placeholders inserted by the path pass; the title pass must not touch them.
    private static readonly Regex PlaceholderRegex = new(
        @"(\[(?:Library \d+|Set Data Folder|Set \d+|Movie \d+)\]|…)",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// Masks <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The report text.</param>
    /// <param name="maskPaths">Replace absolute paths.</param>
    /// <param name="maskTitles">Replace set and movie names.</param>
    /// <param name="terms">Known paths and names.</param>
    /// <returns>The masked text.</returns>
    /// <exception cref="RegexMatchTimeoutException">The text could not be scanned in time.</exception>
    public static string Mask(string text, bool maskPaths, bool maskTitles, ReportMaskTerms terms)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        if (maskPaths)
        {
            result = MaskPaths(result, terms);
        }

        if (maskTitles)
        {
            result = MaskTitles(result, terms);
        }

        return result;
    }

    private static string MaskPaths(string text, ReportMaskTerms terms)
    {
        // Known roots, longest first, so a set folder inside a library wins.
        var roots = new List<(string Root, string? Placeholder)>();
        if (!string.IsNullOrWhiteSpace(terms.SetDataFolder))
        {
            roots.Add((NormalizeRoot(terms.SetDataFolder), SetDataFolderPlaceholder));
        }

        foreach (var root in terms.LibraryRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            roots.Add((NormalizeRoot(root), null));
        }

        roots = roots
            .Where(r => r.Root.Length > 0)
            .GroupBy(r => r.Root, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.Root.Length)
            .ToList();

        var libraryNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return PathRegex.Replace(text, match =>
        {
            var path = match.Value;
            var normalized = path.Replace('\\', '/');

            foreach (var (root, placeholder) in roots)
            {
                if (!StartsWithRoot(normalized, root))
                {
                    continue;
                }

                var name = placeholder ?? "[Library " + GetNumber(libraryNumbers, root) + "]";
                return name + Tail(normalized[root.Length..]);
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0
                ? Ellipsis + "/" + segments[^1]
                : Ellipsis;
        });
    }

    private static string MaskTitles(string text, ReportMaskTerms terms)
    {
        var setNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in terms.SetNames)
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                setNames.Add(trimmed);
            }
        }

        var movieNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in terms.MovieNames)
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed)
                && trimmed.Length >= MinMovieNameLength
                && !setNames.Contains(trimmed))
            {
                movieNames.Add(trimmed);
            }
        }

        if (setNames.Count == 0 && movieNames.Count == 0)
        {
            return text;
        }

        // One pass over all names, longest first, so "Alien Collection" wins over
        // "Alien" and replaced text is never scanned again.
        var alternatives = setNames.Select(n => (Name: n, Pattern: "(?i:" + Regex.Escape(n) + ")"))
            .Concat(movieNames.Select(n => (Name: n, Pattern: Regex.Escape(n))))
            .OrderByDescending(a => a.Name.Length)
            .Select(a => a.Pattern);

        var nameRegex = new Regex(
            @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}_])",
            RegexOptions.CultureInvariant,
            MatchTimeout);

        var setNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var movieNumbers = new Dictionary<string, int>(StringComparer.Ordinal);

        string Replace(Match match)
        {
            var value = match.Value;
            if (setNames.Contains(value))
            {
                return "[Set " + GetNumber(setNumbers, value) + "]";
            }

            return "[Movie " + GetNumber(movieNumbers, value) + "]";
        }

        // Regex.Split with one capture group returns text at even and placeholders
        // at odd indices; only the text is scanned for names.
        var pieces = PlaceholderRegex.Split(text);
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < pieces.Length; i++)
        {
            builder.Append(i % 2 == 1 ? pieces[i] : nameRegex.Replace(pieces[i], Replace));
        }

        return builder.ToString();
    }

    private static string NormalizeRoot(string root)
    {
        return root.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static bool StartsWithRoot(string normalizedPath, string root)
    {
        if (!normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalizedPath.Length == root.Length || normalizedPath[root.Length] == '/';
    }

    private static string Tail(string rest)
    {
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length switch
        {
            0 => string.Empty,
            1 => "/" + segments[0],
            _ => "/" + Ellipsis + "/" + segments[^1]
        };
    }

    private static int GetNumber(Dictionary<string, int> numbers, string key)
    {
        if (!numbers.TryGetValue(key, out var number))
        {
            number = numbers.Count + 1;
            numbers[key] = number;
        }

        return number;
    }
}
