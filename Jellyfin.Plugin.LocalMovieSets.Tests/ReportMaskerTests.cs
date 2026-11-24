using System;
using Jellyfin.Plugin.LocalMovieSets.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class ReportMaskerTests
{
    private static ReportMaskTerms Terms(
        string[]? roots = null,
        string? setFolder = null,
        string[]? sets = null,
        string[]? movies = null) =>
        new(roots ?? [], setFolder, sets ?? [], movies ?? []);

    private static string Paths(string text, ReportMaskTerms? terms = null) =>
        ReportMasker.Mask(text, maskPaths: true, maskTitles: false, terms ?? Terms());

    private static string Titles(string text, ReportMaskTerms terms) =>
        ReportMasker.Mask(text, maskPaths: false, maskTitles: true, terms);

    // ── Paths ────────────────────────────────────────────────────────────

    [Fact]
    public void WindowsPathWithSpaces_KeepsOnlyLastSegment()
    {
        var result = Paths(@"Failed to read `F:\My Movies\Jane Doe\Alien (1979)\Alien.nfo`");

        Assert.Equal("Failed to read `…/Alien.nfo`", result);
    }

    [Fact]
    public void UncPath_IsMasked()
    {
        var result = Paths(@"Folder does not exist or is not accessible: '\\nas\jane\sets\Alien Collection'");

        Assert.Equal("Folder does not exist or is not accessible: '…/Alien Collection'", result);
    }

    [Fact]
    public void PosixPathWithSpacesAndUmlauts_IsMasked()
    {
        var result = Paths("Path '/media/Jürgen Müller/Filme/Bullerbü/movie.nfo' missing");

        Assert.Equal("Path '…/movie.nfo' missing", result);
        Assert.DoesNotContain("Jürgen", result, StringComparison.Ordinal);
    }

    [Fact]
    public void UnquotedPath_SentenceAfterLastSegmentStaysReadable()
    {
        var result = Paths("Movie Set Data Folder is not reachable: /mnt/Jane Doe/sets. Set metadata and artwork are left unchanged until it is back.");

        Assert.Equal("Movie Set Data Folder is not reachable: …/sets. Set metadata and artwork are left unchanged until it is back.", result);
    }

    [Fact]
    public void KnownLibraryRoot_BecomesNumberedPlaceholder()
    {
        var terms = Terms(roots: [@"F:\My Movies", "/mnt/Jane Doe/Filme"]);

        var result = Paths(
            "a '/mnt/Jane Doe/Filme/Alien (1979)/Alien.nfo' b 'F:\\My Movies' c '/mnt/jane doe/filme/x.nfo'",
            terms);

        Assert.Equal("a '[Library 1]/…/Alien.nfo' b '[Library 2]' c '[Library 1]/x.nfo'", result);
    }

    [Fact]
    public void SetDataFolderInsideLibrary_LongestRootWins()
    {
        var terms = Terms(roots: ["/mnt/media"], setFolder: "/mnt/media/_sets/");

        var result = Paths("'/mnt/media/_sets/Alien Collection/poster.jpg'", terms);

        Assert.Equal("'[Set Data Folder]/…/poster.jpg'", result);
    }

    [Theory]
    [InlineData("- **Collections:** +1 / ~2 / -0")]
    [InlineData("(+3 / -1)")]
    [InlineData("Re-read 2 newer movie NFO(s), 1 with changes.")]
    [InlineData("A/B testing and/or more")]
    public void TextWithoutPaths_IsUnchanged(string text)
    {
        Assert.Equal(text, Paths(text));
    }

    // ── Titles ───────────────────────────────────────────────────────────

    [Fact]
    public void SetName_IsMaskedCaseInsensitively_WithStableNumbers()
    {
        var terms = Terms(sets: ["Die Kinder aus Bullerbü Filmreihe", "Alien Collection"]);

        var result = Titles(
            "Updated BoxSet 'Alien Collection'. Created BoxSet 'die kinder aus bullerbü filmreihe'. Again: Alien Collection",
            terms);

        Assert.Equal("Updated BoxSet '[Set 1]'. Created BoxSet '[Set 2]'. Again: [Set 1]", result);
    }

    [Fact]
    public void LongerNameWins_OverContainedName()
    {
        var terms = Terms(sets: ["Alien Collection"], movies: ["Alien"]);

        var result = Titles("Alien Collection contains Alien", terms);

        Assert.Equal("[Set 1] contains [Movie 1]", result);
    }

    [Fact]
    public void OnlyWholeWords_AreMasked()
    {
        var terms = Terms(movies: ["Alien"]);

        Assert.Equal("Aliens and Alienated", Titles("Aliens and Alienated", terms));
    }

    [Fact]
    public void MovieNames_AreCaseSensitive_AndShortTitlesAreSkipped()
    {
        var terms = Terms(movies: ["Her", "Up"]);

        var result = Titles("Her, her, Up to date", terms);

        Assert.Equal("[Movie 1], her, Up to date", result);
    }

    [Fact]
    public void KeptMoviesLogLine_IsMasked()
    {
        var terms = Terms(movies: ["Alien Resurrection", "Aliens"]);

        var result = Titles(
            "[12:00:00 WRN] Kept 2 movie(s) in their current collection because their NFO could not be read: Alien Resurrection, Aliens",
            terms);

        Assert.EndsWith("could not be read: [Movie 1], [Movie 2]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NameWithRegexCharacters_IsMaskedLiterally()
    {
        var terms = Terms(sets: ["[REC] Collection (2007+)"]);

        Assert.Equal("x '[Set 1]' y", Titles("x '[REC] Collection (2007+)' y", terms));
    }

    // ── Both ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidatorMessageWithPathAndSetName_IsFullyMasked()
    {
        var terms = Terms(setFolder: "/srv/Jane/sets", sets: ["Alien Collection"], movies: ["Library"]);

        var result = ReportMasker.Mask(
            "- [Error] **Alien Collection**: Folder does not exist or is not accessible: '/srv/Jane/sets/Alien Collection'",
            maskPaths: true,
            maskTitles: true,
            terms);

        Assert.Equal("- [Error] **[Set 1]**: Folder does not exist or is not accessible: '[Set Data Folder]/[Set 1]'", result);
    }

    [Fact]
    public void PlaceholdersFromPathPass_AreNotChangedByTitlePass()
    {
        var terms = Terms(roots: ["/data/films"], movies: ["Library", "Movie"]);

        var result = ReportMasker.Mask("'/data/films/a/b.nfo' Library", true, true, terms);

        Assert.Equal("'[Library 1]/…/b.nfo' [Movie 1]", result);
    }

    [Fact]
    public void BothOptionsOff_ReturnsTextUnchanged()
    {
        const string text = "'/mnt/Jane/x/y.nfo' Alien Collection";
        var terms = Terms(sets: ["Alien Collection"]);

        Assert.Equal(text, ReportMasker.Mask(text, false, false, terms));
    }
}
