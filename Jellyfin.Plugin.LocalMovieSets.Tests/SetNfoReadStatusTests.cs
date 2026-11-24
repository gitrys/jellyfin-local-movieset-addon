using System;
using System.IO;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

public class SetNfoReadStatusTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SetNfoParser _parser;

    public SetNfoReadStatusTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalMovieSets_ReadStatus_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _parser = new SetNfoParser(NullLogger<SetNfoParser>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private void WriteSetNfo(string setName, string content)
    {
        var folder = Path.Combine(_tempDirectory, setName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{setName}.nfo"), content);
    }

    [Fact]
    public void ReadSet_NoFolderConfigured_NotConfigured()
    {
        var result = _parser.ReadSet(string.Empty, "Alien Collection", NfoNamingConvention.SetSubfolder);

        Assert.Equal(SetNfoReadStatus.NotConfigured, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public void ReadSet_FolderMissing_FolderUnavailable()
    {
        var missing = Path.Combine(_tempDirectory, "offline-share");

        var result = _parser.ReadSet(missing, "Alien Collection", NfoNamingConvention.SetSubfolder);

        Assert.Equal(SetNfoReadStatus.FolderUnavailable, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public void ReadSet_NoNfoForSet_NotFound()
    {
        var result = _parser.ReadSet(_tempDirectory, "Alien Collection", NfoNamingConvention.SetSubfolder);

        Assert.Equal(SetNfoReadStatus.NotFound, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public void ReadSet_BrokenNfo_Unreadable()
    {
        WriteSetNfo("Alien Collection", "<set><title>Alien</title><genre>Horror");

        var result = _parser.ReadSet(_tempDirectory, "Alien Collection", NfoNamingConvention.SetSubfolder);

        Assert.Equal(SetNfoReadStatus.Unreadable, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public void ReadSet_ValidNfoWithoutGenres_OkWithEmptyList()
    {
        WriteSetNfo("Alien Collection", "<set><title>Alien Collection</title></set>");

        var result = _parser.ReadSet(_tempDirectory, "Alien Collection", NfoNamingConvention.SetSubfolder);

        Assert.Equal(SetNfoReadStatus.Ok, result.Status);
        Assert.NotNull(result.Info);
        Assert.Empty(result.Info.Genres);
    }

    [Fact]
    public void IsFolderUnavailable_Cases()
    {
        Assert.False(SetNfoParser.IsFolderUnavailable(null));
        Assert.False(SetNfoParser.IsFolderUnavailable("  "));
        Assert.False(SetNfoParser.IsFolderUnavailable(_tempDirectory));
        Assert.True(SetNfoParser.IsFolderUnavailable(Path.Combine(_tempDirectory, "offline-share")));
    }
}
