using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalMovieSets.Services.Validation;

/// <summary>
/// Severity level of a validation diagnostic finding.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Critical failure preventing the set from being imported or parsed (e.g. malformed XML, empty file).
    /// </summary>
    Error,

    /// <summary>
    /// Missing or incomplete data that impairs display (e.g. missing poster, missing title).
    /// </summary>
    Warning,

    /// <summary>
    /// Informational note or naming recommendation (e.g. folder without NFO, non-standard artwork naming).
    /// </summary>
    Info
}

/// <summary>
/// Category of a validation diagnostic issue.
/// </summary>
public enum ValidationCategory
{
    /// <summary>
    /// XML is syntactically invalid or failed to parse.
    /// </summary>
    XmlSyntax,

    /// <summary>
    /// File exists but contains 0 bytes or no XML root.
    /// </summary>
    EmptyFile,

    /// <summary>
    /// Mandatory metadata is missing (e.g. no title/name).
    /// </summary>
    MissingTitle,

    /// <summary>
    /// Primary poster or backdrop fanart is missing.
    /// </summary>
    MissingArtwork,

    /// <summary>
    /// Non-standard file name (e.g. cover.jpg) that might be ignored.
    /// </summary>
    NonStandardArtwork,

    /// <summary>
    /// Subfolder has no NFO file at all.
    /// </summary>
    FolderWithoutNfo,

    /// <summary>
    /// Multiple distinct folders or NFOs define identical set names.
    /// </summary>
    DuplicateName,

    /// <summary>
    /// Theme song audio file discovery or status.
    /// </summary>
    ThemeSong
}

/// <summary>
/// Represents a single diagnostic issue discovered during validation.
/// </summary>
public class ValidationIssueDto
{
    /// <summary>
    /// Gets or sets the severity level.
    /// </summary>
    public ValidationSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets the issue category.
    /// </summary>
    public ValidationCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the name of the collection, if resolved.
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// Gets or sets the path of the set folder being inspected.
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative or full path of the affected file (if applicable).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets the line number where the issue occurred (for XML errors).
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Gets or sets the human-readable explanation of the issue.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a concrete recommendation on how to resolve the issue.
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// Full report produced by the local NFO and artwork validation scan.
/// </summary>
public class ValidationReportDto
{
    /// <summary>
    /// Gets or sets the total number of set folders scanned.
    /// </summary>
    public int ScannedSetsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of sets that passed all checks with zero issues.
    /// </summary>
    public int HealthySetsCount { get; set; }

    /// <summary>
    /// Gets or sets the count of critical error findings.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the count of warning findings.
    /// </summary>
    public int WarningCount { get; set; }

    /// <summary>
    /// Gets or sets the count of informational findings.
    /// </summary>
    public int InfoCount { get; set; }

    /// <summary>
    /// Gets or sets the execution time of the validation scan in milliseconds.
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the list of diagnostic issues discovered during the scan.
    /// </summary>
    public List<ValidationIssueDto> Issues { get; set; } = new();
}
