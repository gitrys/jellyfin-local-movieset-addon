using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Services.Validation;

/// <summary>
/// Scans the Movie Set Data Folder and reports what the sync and the image
/// provider will actually use: the set NFO the configured naming convention
/// points to, artwork names from <see cref="BoxSetImageProvider.ArtworkMappings"/>,
/// and the theme file names the sync copies. Only the set folders directly inside
/// the data folder are checked; nested folders are reported, not walked.
/// </summary>
public class NfoValidator
{
    // Files that look like a poster/fanart but are not read from a set folder.
    private static readonly string[] UnusedPosterNames =
    [
        "movieset-poster.jpg", "movieset-poster.png", "movieset-folder.jpg", "movieset-folder.png",
        "movieset-cover.jpg", "cover.jpg", "cover.png", "front.jpg", "front.png"
    ];

    private static readonly string[] UnusedFanartNames =
    [
        "movieset-fanart.jpg", "movieset-fanart.png", "movieset-backdrop.jpg", "movieset-backdrop.png"
    ];

    private readonly ILogger<NfoValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NfoValidator"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public NfoValidator(ILogger<NfoValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans the specified folder and validates each set folder the way the sync reads it.
    /// </summary>
    /// <param name="targetFolder">Path to the folder to validate (defaults to configured SetDataFolder if null).</param>
    /// <param name="naming">NFO naming convention (defaults to the saved setting).</param>
    /// <returns>A comprehensive <see cref="ValidationReportDto"/> with all findings.</returns>
    public ValidationReportDto Validate(string? targetFolder = null, NfoNamingConvention? naming = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new ValidationReportDto();

        var folder = string.IsNullOrWhiteSpace(targetFolder)
            ? Plugin.Instance?.Configuration.SetDataFolder
            : targetFolder;
        var nfoNaming = naming ?? Plugin.Instance?.Configuration.NfoNaming ?? NfoNamingConvention.SetSubfolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            report.Issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.FolderWithoutNfo,
                Message = "No Movie Set Data Folder is configured or specified.",
                Suggestion = "Configure a valid Movie Set Data Folder in the plugin settings."
            });
            sw.Stop();
            report.ExecutionTimeMs = sw.ElapsedMilliseconds;
            report.ErrorCount = 1;
            return report;
        }

        if (!Directory.Exists(folder))
        {
            report.Issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.FolderWithoutNfo,
                FolderPath = folder,
                Message = $"Folder does not exist or is not accessible: '{folder}'",
                Suggestion = "Verify that the path is correct, the drive is mounted, and permissions allow reading."
            });
            sw.Stop();
            report.ExecutionTimeMs = sw.ElapsedMilliseconds;
            report.ErrorCount = 1;
            return report;
        }

        _logger.LogInformation("Starting NFO & Artwork validation scan in '{Folder}' ({Naming})", folder, nfoNaming);

        var subdirs = Directory.GetDirectories(folder);
        var rootNfos = Directory.GetFiles(folder, "*.nfo", SearchOption.TopDirectoryOnly);

        var definedCollections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalSetsScanned = 0;
        var setsWithIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Set folders directly inside the data folder (the only layout the sync reads)
        foreach (var subdir in subdirs)
        {
            totalSetsScanned++;
            var dirName = Path.GetFileName(subdir);
            var folderIssues = ValidateDirectory(folder, subdir, dirName, nfoNaming, definedCollections);
            if (folderIssues.Any(i => i.Severity != ValidationSeverity.Info))
            {
                setsWithIssues.Add(dirName);
            }

            report.Issues.AddRange(folderIssues);
        }

        // 2. NFOs in the data folder root: no longer a supported layout, the sync ignores them
        foreach (var nfoPath in rootNfos)
        {
            totalSetsScanned++;
            var fileName = Path.GetFileName(nfoPath);
            var setBaseName = Path.GetFileNameWithoutExtension(nfoPath);
            var expected = Path.GetFileName(SetNfoParser.GetNfoPath(folder, setBaseName, nfoNaming));
            setsWithIssues.Add("\0root:" + fileName);
            report.Issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.UnsupportedLayout,
                CollectionName = setBaseName,
                FolderPath = folder,
                FilePath = nfoPath,
                Message = $"NFO '{fileName}' is in the root of the Movie Set Data Folder. The sync does not read NFOs or artwork there.",
                Suggestion = $"Move it into its own folder: '{setBaseName}/{expected}', together with its artwork (poster.jpg, fanart.jpg)."
            });
        }

        sw.Stop();
        report.ScannedSetsCount = totalSetsScanned;
        report.HealthySetsCount = Math.Max(0, totalSetsScanned - setsWithIssues.Count);
        report.ErrorCount = report.Issues.Count(i => i.Severity == ValidationSeverity.Error);
        report.WarningCount = report.Issues.Count(i => i.Severity == ValidationSeverity.Warning);
        report.InfoCount = report.Issues.Count(i => i.Severity == ValidationSeverity.Info);
        report.ExecutionTimeMs = sw.ElapsedMilliseconds;

        _logger.LogInformation(
            "Validation finished: {Scanned} sets scanned, {Healthy} healthy, {Errors} errors, {Warnings} warnings, {Infos} infos ({Elapsed} ms)",
            report.ScannedSetsCount,
            report.HealthySetsCount,
            report.ErrorCount,
            report.WarningCount,
            report.InfoCount,
            report.ExecutionTimeMs);

        return report;
    }

    /// <summary>
    /// Validates one set folder directly inside the data folder. Does not recurse:
    /// the sync never reads nested folders, so they are only reported.
    /// </summary>
    private List<ValidationIssueDto> ValidateDirectory(
        string dataFolder,
        string directoryPath,
        string folderName,
        NfoNamingConvention naming,
        Dictionary<string, string> definedCollections)
    {
        var issues = new List<ValidationIssueDto>();

        // A real folder name has no characters the sanitizer changes, so this is
        // exactly the file the sync reads for a set with this folder name.
        var expectedNfo = SetNfoParser.GetNfoPath(dataFolder, folderName, naming);
        var expectedName = Path.GetFileName(expectedNfo);

        if (File.Exists(expectedNfo))
        {
            issues.AddRange(ValidateSingleNfo(expectedNfo, folderName, directoryPath, definedCollections));
        }
        else
        {
            var otherNfos = Directory.GetFiles(directoryPath, "*.nfo", SearchOption.TopDirectoryOnly);
            if (otherNfos.Length > 0)
            {
                var found = string.Join(", ", otherNfos.Select(Path.GetFileName));
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Error,
                    Category = ValidationCategory.NamingMismatch,
                    CollectionName = folderName,
                    FolderPath = directoryPath,
                    FilePath = otherNfos[0],
                    Message = $"The sync looks for '{expectedName}' in this folder (NFO naming: {naming}), but only found: {found}.",
                    Suggestion = $"Rename the set NFO to '{expectedName}', or use Detect Naming to pick the matching NFO naming setting."
                });
            }
            else if (HasNfoInChildFolder(directoryPath))
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Warning,
                    Category = ValidationCategory.UnsupportedLayout,
                    CollectionName = folderName,
                    FolderPath = directoryPath,
                    Message = "Folder has no set NFO, but its subfolders contain NFO files. The sync does not read nested set folders.",
                    Suggestion = "Move each set into its own folder directly inside the Movie Set Data Folder."
                });

                // A container folder, not a set: no artwork check.
                return issues;
            }
            else
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Info,
                    Category = ValidationCategory.FolderWithoutNfo,
                    CollectionName = folderName,
                    FolderPath = directoryPath,
                    Message = $"Folder contains no set NFO ('{expectedName}'). Artwork in it is still used for a set with this name.",
                    Suggestion = $"Add '{expectedName}' for title, overview, genres and studios, or remove the folder if it is not a set."
                });
            }
        }

        issues.AddRange(ValidateFolderArtwork(directoryPath, folderName));
        return issues;
    }

    private static bool HasNfoInChildFolder(string directoryPath)
    {
        return Directory.EnumerateDirectories(directoryPath)
            .Any(child => Directory.EnumerateFiles(child, "*.nfo", SearchOption.TopDirectoryOnly).Any());
    }

    private List<ValidationIssueDto> ValidateSingleNfo(
        string nfoPath,
        string fallbackName,
        string folderPath,
        Dictionary<string, string> definedCollections)
    {
        var issues = new List<ValidationIssueDto>();
        var fileName = Path.GetFileName(nfoPath);
        var fileInfo = new FileInfo(nfoPath);

        // 1. Check empty file (0 bytes)
        if (fileInfo.Length == 0)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.EmptyFile,
                CollectionName = fallbackName,
                FolderPath = folderPath,
                FilePath = nfoPath,
                Message = $"NFO file is completely empty (0 bytes).",
                Suggestion = "Regenerate the NFO file with your media manager (e.g. tinyMediaManager) or recreate valid XML."
            });
            return issues;
        }

        // 2. Validate XML parsing
        XDocument doc;
        try
        {
            doc = NfoXmlLoader.Load(nfoPath);
        }
        catch (XmlException ex)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.XmlSyntax,
                CollectionName = fallbackName,
                FolderPath = folderPath,
                FilePath = nfoPath,
                LineNumber = ex.LineNumber,
                Message = $"XML syntax error at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}",
                Suggestion = $"Open '{fileName}' and fix the XML syntax around line {ex.LineNumber} (ensure tags are properly closed and special characters like '&' are escaped as '&amp;')."
            });
            return issues;
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.XmlSyntax,
                CollectionName = fallbackName,
                FolderPath = folderPath,
                FilePath = nfoPath,
                Message = $"Failed to read NFO file: {ex.Message}",
                Suggestion = "Check file encoding and ensure file contains valid XML text."
            });
            return issues;
        }

        var root = doc.Root;
        if (root == null)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.EmptyFile,
                CollectionName = fallbackName,
                FolderPath = folderPath,
                FilePath = nfoPath,
                Message = "NFO file has no XML root element.",
                Suggestion = "Ensure file starts with a valid XML root tag such as <movie>, <set>, or <collection>."
            });
            return issues;
        }

        var rootName = root.Name.LocalName.ToLowerInvariant();
        string? collectionTitle = null;

        // 3. Inspect collection metadata depending on root tag
        if (rootName is "movie")
        {
            // In movie NFO, collection is inside <set><name>...</name></set>
            var setElem = root.Element("set");
            if (setElem != null)
            {
                var nameVal = setElem.Element("name")?.Value?.Trim() ?? setElem.Element("title")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(nameVal))
                {
                    issues.Add(new ValidationIssueDto
                    {
                        Severity = ValidationSeverity.Warning,
                        Category = ValidationCategory.MissingTitle,
                        CollectionName = fallbackName,
                        FolderPath = folderPath,
                        FilePath = nfoPath,
                        Message = "Movie NFO defines a <set> tag, but the <name> or <title> element inside is empty.",
                        Suggestion = "Add a valid collection name inside <set><name>Collection Name</name></set>."
                    });
                }
                else
                {
                    collectionTitle = nameVal;
                }
            }
        }
        else if (rootName is "set" or "collection")
        {
            // Dedicated set NFO
            var titleVal = root.Element("title")?.Value?.Trim() ?? root.Element("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(titleVal))
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Warning,
                    Category = ValidationCategory.MissingTitle,
                    CollectionName = fallbackName,
                    FolderPath = folderPath,
                    FilePath = nfoPath,
                    Message = $"Dedicated set NFO <{rootName}> is missing a <title> or <name> element.",
                    Suggestion = "Add a <title>...</title> element to define the display name of the collection."
                });
            }
            else
            {
                collectionTitle = titleVal;
            }
        }

        // 4. Duplicate name detection
        if (!string.IsNullOrEmpty(collectionTitle))
        {
            if (definedCollections.TryGetValue(collectionTitle, out var existingPath) &&
                !string.Equals(existingPath, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Warning,
                    Category = ValidationCategory.DuplicateName,
                    CollectionName = collectionTitle,
                    FolderPath = folderPath,
                    FilePath = nfoPath,
                    Message = $"Duplicate collection name '{collectionTitle}' detected. Also defined in '{existingPath}'.",
                    Suggestion = "Ensure different movie sets have unique names, or merge them into a single collection folder."
                });
            }
            else
            {
                definedCollections[collectionTitle] = folderPath;
            }
        }

        return issues;
    }


    /// <summary>
    /// Checks artwork and theme music with the same file names and lookups
    /// (<see cref="File.Exists"/>, exact names) as the image provider and the sync.
    /// </summary>
    private static List<ValidationIssueDto> ValidateFolderArtwork(string directoryPath, string collectionName)
    {
        var issues = new List<ValidationIssueDto>();
        var fileNames = Directory.GetFiles(directoryPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        // 1. Primary poster
        var posterNames = GetArtworkNames(ImageType.Primary);
        if (BoxSetImageProvider.FindFirstExistingFile(directoryPath, posterNames) is null)
        {
            var unused = FindUnusedArtwork(fileNames, posterNames, UnusedPosterNames);
            if (unused != null)
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Warning,
                    Category = ValidationCategory.NonStandardArtwork,
                    CollectionName = collectionName,
                    FolderPath = directoryPath,
                    FilePath = Path.Combine(directoryPath, unused),
                    Message = $"'{unused}' is not used as the collection poster. In a set folder only {string.Join(", ", posterNames)} are read (exact names).",
                    Suggestion = $"Rename '{unused}' to 'poster.jpg' (or 'poster.png' for a PNG file)."
                });
            }
            else
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Warning,
                    Category = ValidationCategory.MissingArtwork,
                    CollectionName = collectionName,
                    FolderPath = directoryPath,
                    Message = "No primary poster image (e.g. 'poster.jpg', 'folder.jpg') found in set folder.",
                    Suggestion = "Add a 'poster.jpg' to this folder to avoid Jellyfin generating an automatic composite collage."
                });
            }
        }

        // 2. Backdrop / fanart
        var fanartNames = GetArtworkNames(ImageType.Backdrop);
        if (BoxSetImageProvider.FindFirstExistingFile(directoryPath, fanartNames) is null)
        {
            var unused = FindUnusedArtwork(fileNames, fanartNames, UnusedFanartNames);
            issues.Add(unused != null
                ? new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Info,
                    Category = ValidationCategory.NonStandardArtwork,
                    CollectionName = collectionName,
                    FolderPath = directoryPath,
                    FilePath = Path.Combine(directoryPath, unused),
                    Message = $"'{unused}' is not used as the collection backdrop. In a set folder only {string.Join(", ", fanartNames)} are read (exact names).",
                    Suggestion = $"Rename '{unused}' to 'fanart.jpg' (or 'fanart.png' for a PNG file)."
                }
                : new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Info,
                    Category = ValidationCategory.MissingArtwork,
                    CollectionName = collectionName,
                    FolderPath = directoryPath,
                    Message = "No background fanart image (e.g. 'fanart.jpg', 'backdrop.jpg') found in set folder.",
                    Suggestion = "Add a 'fanart.jpg' to provide a high-resolution backdrop when viewing this collection."
                });
        }

        // 3. Theme music: same candidates as the sync (theme.*, <folder>-theme.*, <folder>.theme.*)
        var themePath = BoxSetImageProvider.FindFirstExistingFile(
            directoryPath,
            SetNfoParser.GetThemeSongFileNameCandidates(Path.GetFileName(directoryPath)));
        if (themePath != null)
        {
            var themeFile = Path.GetFileName(themePath);
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Info,
                Category = ValidationCategory.ThemeSong,
                CollectionName = collectionName,
                FolderPath = directoryPath,
                FilePath = themePath,
                Message = $"Theme song found: '{themeFile}'.",
                Suggestion = "Theme music will be played automatically when browsing this collection."
            });
        }

        return issues;
    }

    private static string[] GetArtworkNames(ImageType type)
    {
        return BoxSetImageProvider.ArtworkMappings.First(m => m.ImageType == type).FileNames;
    }

    /// <summary>
    /// Returns a file that looks like artwork of this kind but is not read: a
    /// supported name with different letter case (case-sensitive file systems)
    /// or a known unsupported name.
    /// </summary>
    private static string? FindUnusedArtwork(List<string> fileNames, string[] supportedNames, string[] unusedNames)
    {
        return fileNames.FirstOrDefault(f => supportedNames.Contains(f, StringComparer.OrdinalIgnoreCase))
            ?? fileNames.FirstOrDefault(f => unusedNames.Contains(f, StringComparer.OrdinalIgnoreCase));
    }
}
