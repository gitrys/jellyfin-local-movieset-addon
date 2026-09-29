using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalMovieSets.Services.Validation;

/// <summary>
/// Service that scans and validates local NFO files and artwork files in
/// movie set data folders or movie directories.
/// </summary>
public class NfoValidator
{
    private static readonly string[] StandardPosterNames =
    [
        "poster.jpg", "poster.png", "folder.jpg", "folder.png",
        "movieset-poster.jpg", "movieset-poster.png", "movieset-folder.jpg"
    ];

    private static readonly string[] StandardFanartNames =
    [
        "fanart.jpg", "fanart.png", "backdrop.jpg", "backdrop.png",
        "movieset-fanart.jpg", "movieset-fanart.png", "movieset-backdrop.jpg"
    ];

    private static readonly string[] NonStandardPosterHints =
    [
        "cover.jpg", "cover.png", "movieset-cover.jpg", "front.jpg", "front.png"
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
    /// Scans the specified folder and performs deep validation on all NFO files and artwork.
    /// </summary>
    /// <param name="targetFolder">Path to the folder to validate (defaults to configured SetDataFolder if null).</param>
    /// <returns>A comprehensive <see cref="ValidationReportDto"/> with all findings.</returns>
    public ValidationReportDto Validate(string? targetFolder = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new ValidationReportDto();

        var folder = string.IsNullOrWhiteSpace(targetFolder)
            ? Plugin.Instance?.Configuration.SetDataFolder
            : targetFolder;

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

        _logger.LogInformation("Starting NFO & Artwork validation scan in '{Folder}'", folder);

        var subdirs = Directory.GetDirectories(folder);
        var directNfos = Directory.GetFiles(folder, "*.nfo", SearchOption.TopDirectoryOnly);

        var definedCollections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalSetsScanned = 0;
        var setsWithIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan direct subfolders (e.g. SetSubfolder, CollectionNfo, or movie folders)
        foreach (var subdir in subdirs)
        {
            totalSetsScanned++;
            var dirName = Path.GetFileName(subdir);
            var folderIssues = ValidateDirectory(subdir, dirName, definedCollections);
            if (folderIssues.Any(i => i.Severity != ValidationSeverity.Info))
            {
                setsWithIssues.Add(dirName);
            }
            if (folderIssues.Count > 0)
            {
                report.Issues.AddRange(folderIssues);
            }
        }

        // 2. Scan root NFOs if NfoInParent style is used
        if (directNfos.Length > 0)
        {
            foreach (var nfoPath in directNfos)
            {
                totalSetsScanned++;
                var fileName = Path.GetFileName(nfoPath);
                var setBaseName = Path.GetFileNameWithoutExtension(nfoPath);
                var nfoIssues = ValidateSingleNfo(nfoPath, setBaseName, folder, definedCollections);
                
                // Also check sibling artwork: <setBaseName>-poster.jpg etc.
                var artworkIssues = ValidateSiblingArtwork(folder, setBaseName);
                nfoIssues.AddRange(artworkIssues);

                if (nfoIssues.Any(i => i.Severity != ValidationSeverity.Info))
                {
                    setsWithIssues.Add(setBaseName);
                }
                if (nfoIssues.Count > 0)
                {
                    report.Issues.AddRange(nfoIssues);
                }
            }
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

    private List<ValidationIssueDto> ValidateDirectory(
        string directoryPath,
        string setNameFallback,
        Dictionary<string, string> definedCollections)
    {
        var issues = new List<ValidationIssueDto>();
        var nfoFiles = Directory.GetFiles(directoryPath, "*.nfo", SearchOption.TopDirectoryOnly);

        // Check if this subfolder has child movie folders (e.g. "00 Test Universum / 00 Test Universum Teil 1")
        var childSubdirs = Directory.GetDirectories(directoryPath);
        if (nfoFiles.Length == 0 && childSubdirs.Length > 0)
        {
            // Inspect child subdirectories recursively
            var childSetsFound = 0;
            foreach (var childSubdir in childSubdirs)
            {
                var childName = Path.GetFileName(childSubdir);
                var childIssues = ValidateDirectory(childSubdir, childName, definedCollections);
                if (childIssues.Count > 0)
                {
                    issues.AddRange(childIssues);
                }
                childSetsFound++;
            }

            if (childSetsFound > 0)
            {
                return issues;
            }
        }

        // If no NFO file exists in this directory:
        if (nfoFiles.Length == 0)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Info,
                Category = ValidationCategory.FolderWithoutNfo,
                CollectionName = setNameFallback,
                FolderPath = directoryPath,
                Message = $"Folder contains no .nfo file.",
                Suggestion = "Create a collection .nfo file (or movie .nfo) in this folder or remove empty folder."
            });
            return issues;
        }

        string? resolvedCollectionName = null;

        // Validate each NFO in this directory
        foreach (var nfoPath in nfoFiles)
        {
            var singleIssues = ValidateSingleNfo(nfoPath, setNameFallback, directoryPath, definedCollections);
            issues.AddRange(singleIssues);

            if (resolvedCollectionName == null)
            {
                var matched = singleIssues.FirstOrDefault(i => !string.IsNullOrEmpty(i.CollectionName))?.CollectionName;
                if (!string.IsNullOrEmpty(matched))
                {
                    resolvedCollectionName = matched;
                }
            }
        }

        resolvedCollectionName ??= setNameFallback;

        // Validate artwork files in this folder
        var artworkIssues = ValidateFolderArtwork(directoryPath, resolvedCollectionName);
        issues.AddRange(artworkIssues);

        return issues;
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

    private List<ValidationIssueDto> ValidateFolderArtwork(string directoryPath, string collectionName)
    {
        var issues = new List<ValidationIssueDto>();
        var files = Directory.GetFiles(directoryPath);
        var fileNamesLower = files.Select(Path.GetFileName).Where(f => f != null).Select(f => f!.ToLowerInvariant()).ToHashSet();

        // 1. Primary Poster Check
        var hasPoster = StandardPosterNames.Any(p => fileNamesLower.Contains(p));
        if (!hasPoster)
        {
            // Check if non-standard poster name exists (e.g. cover.jpg)
            var nonStandardFound = NonStandardPosterHints.FirstOrDefault(h => fileNamesLower.Contains(h));
            if (nonStandardFound != null)
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = ValidationSeverity.Info,
                    Category = ValidationCategory.NonStandardArtwork,
                    CollectionName = collectionName,
                    FolderPath = directoryPath,
                    FilePath = Path.Combine(directoryPath, nonStandardFound),
                    Message = $"Non-standard artwork '{nonStandardFound}' found in set folder.",
                    Suggestion = $"Rename '{nonStandardFound}' to 'poster.jpg' for full compatibility with Jellyfin and Kodi."
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

        // 2. Backdrop / Fanart Check
        var hasFanart = StandardFanartNames.Any(f => fileNamesLower.Contains(f));
        if (!hasFanart)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Info,
                Category = ValidationCategory.MissingArtwork,
                CollectionName = collectionName,
                FolderPath = directoryPath,
                Message = "No background fanart image (e.g. 'fanart.jpg', 'backdrop.jpg') found in set folder.",
                Suggestion = "Add a 'fanart.jpg' to provide a high-resolution backdrop when viewing this collection."
            });
        }

        return issues;
    }

    private List<ValidationIssueDto> ValidateSiblingArtwork(string folderPath, string setBaseName)
    {
        var issues = new List<ValidationIssueDto>();
        var posterCandidates = new[]
        {
            $"{setBaseName}-poster.jpg", $"{setBaseName}-poster.png",
            $"{setBaseName}-folder.jpg", $"{setBaseName}-folder.png"
        };
        var fanartCandidates = new[]
        {
            $"{setBaseName}-fanart.jpg", $"{setBaseName}-fanart.png",
            $"{setBaseName}-backdrop.jpg", $"{setBaseName}-backdrop.png"
        };

        var hasPoster = posterCandidates.Any(c => File.Exists(Path.Combine(folderPath, c)));
        if (!hasPoster)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Warning,
                Category = ValidationCategory.MissingArtwork,
                CollectionName = setBaseName,
                FolderPath = folderPath,
                Message = $"No sibling poster '{setBaseName}-poster.jpg' found for set '{setBaseName}'.",
                Suggestion = $"Place '{setBaseName}-poster.jpg' next to the NFO file."
            });
        }

        var hasFanart = fanartCandidates.Any(c => File.Exists(Path.Combine(folderPath, c)));
        if (!hasFanart)
        {
            issues.Add(new ValidationIssueDto
            {
                Severity = ValidationSeverity.Info,
                Category = ValidationCategory.MissingArtwork,
                CollectionName = setBaseName,
                FolderPath = folderPath,
                Message = $"No sibling fanart '{setBaseName}-fanart.jpg' found for set '{setBaseName}'.",
                Suggestion = $"Place '{setBaseName}-fanart.jpg' next to the NFO file."
            });
        }

        return issues;
    }
}
