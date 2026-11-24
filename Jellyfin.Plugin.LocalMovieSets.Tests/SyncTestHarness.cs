using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.LocalMovieSets.Tests;

/// <summary>
/// Tests in this collection set process-wide static state (<see cref="Plugin.Instance"/>,
/// <see cref="BaseItem.LibraryManager"/>), so they run one at a time, after the parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PluginStateCollection
{
    public const string Name = "Plugin static state";
}

/// <summary>
/// A <see cref="LocalMovieSetManager"/> wired to substitutes for Jellyfin's services,
/// a real <see cref="Plugin"/> whose configuration and history live in a temp folder,
/// and movie folders with real NFO files read by the real <see cref="MovieNfoParser"/>.
/// The substitutes keep a small in-memory library and record every write.
/// </summary>
public sealed class SyncTestHarness : IDisposable
{
    private readonly ILibraryManager? _previousStaticLibraryManager;

    /// <param name="withActivityManager"><c>false</c> to construct the manager without an activity manager.</param>
    public SyncTestHarness(bool withActivityManager = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "LocalMovieSets_Sync_" + Guid.NewGuid().ToString("N"));
        LibraryDir = Directory.CreateDirectory(Path.Combine(Root, "movies")).FullName;

        // Mount Guard aborts on an empty library root; keep it non-empty unless a test wants otherwise.
        File.WriteAllText(Path.Combine(LibraryDir, ".keep"), string.Empty);
        SetDataFolder = Directory.CreateDirectory(Path.Combine(Root, "sets")).FullName;
        var configDir = Directory.CreateDirectory(Path.Combine(Root, "config")).FullName;
        var pluginsDir = Directory.CreateDirectory(Path.Combine(Root, "plugins")).FullName;

        Config = new PluginConfiguration
        {
            SetDataFolder = SetDataFolder,
            NfoNaming = NfoNamingConvention.SetSubfolder,
            EnableMountGuard = true,
            MinimumMovies = 2,
            DeleteOrphanedSets = false,
            EnableMovieFolderArtworkFallback = false,
            LegacySortTitleCleanupCompleted = true
        };

        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginConfigurationsPath.Returns(configDir);
        paths.PluginsPath.Returns(pluginsDir);
        var xml = Substitute.For<IXmlSerializer>();
        xml.DeserializeFromFile(typeof(PluginConfiguration), Arg.Any<string>()).Returns(_ => Config);
        Plugin = new Plugin(paths, xml);

        Library.GetItemsResult(Arg.Any<InternalItemsQuery>())
            .Returns(ci => new QueryResult<BaseItem>(Query(ci.Arg<InternalItemsQuery>())));
        LibraryLocations.Add(LibraryDir);
        Library.GetVirtualFolders().Returns(_ =>
            new List<VirtualFolderInfo>
            {
                new() { Name = "Movies", CollectionType = CollectionTypeOptions.movies, Locations = LibraryLocations.ToArray() }
            }
            .Concat(ExtraLibraries)
            .ToList());
        Library.When(l => l.DeleteItem(Arg.Any<BaseItem>(), Arg.Any<DeleteOptions>()))
            .Do(ci =>
            {
                var item = ci.Arg<BaseItem>();
                if (FailDeleteFor.Contains(item.Name))
                {
                    throw new IOException("locked");
                }

                Deleted.Add(item);
                BoxSets.Remove((BoxSet)item);
            });

        Collections.CreateCollectionAsync(Arg.Any<CollectionCreationOptions>()).Returns(ci =>
        {
            DuringSync?.Invoke();
            var options = ci.Arg<CollectionCreationOptions>();
            var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = options.Name };
            BoxSets.Add(boxSet);
            Members[boxSet.Id] = options.ItemIdList.Select(id => Movies.First(m => m.Id == Guid.Parse(id))).ToList();
            Created.Add(options);
            return Task.FromResult(boxSet);
        });
        Collections.AddToCollectionAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>()).Returns(ci =>
        {
            var ids = ci.Arg<IEnumerable<Guid>>().ToArray();
            Added.Add((ci.Arg<Guid>(), ids));
            Members[ci.Arg<Guid>()].AddRange(Movies.Where(m => ids.Contains(m.Id)));
            return Task.CompletedTask;
        });
        Collections.RemoveFromCollectionAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>()).Returns(ci =>
        {
            var ids = ci.Arg<IEnumerable<Guid>>().ToArray();
            Removed.Add((ci.Arg<Guid>(), ids));
            Members[ci.Arg<Guid>()].RemoveAll(m => ids.Contains(m.Id));
            return Task.CompletedTask;
        });

        // Movie.UpdateToRepositoryAsync goes through Jellyfin's static library manager.
        _previousStaticLibraryManager = BaseItem.LibraryManager;
        BaseItem.LibraryManager = Library;

        Manager = new LocalMovieSetManager(
            Library,
            Collections,
            Providers,
            Substitute.For<IFileSystem>(),
            new MovieNfoParser(NullLogger<MovieNfoParser>.Instance),
            NullLogger<LocalMovieSetManager>.Instance,
            withActivityManager ? Activity : null);
    }

    public string Root { get; }

    public string LibraryDir { get; }

    public string SetDataFolder { get; }

    public PluginConfiguration Config { get; }

    /// <summary>Gets the paths of the movie library (Mount Guard checks them).</summary>
    public List<string> LibraryLocations { get; } = [];

    /// <summary>Gets further libraries returned besides the movie library.</summary>
    public List<VirtualFolderInfo> ExtraLibraries { get; } = [];

    /// <summary>Gets or sets an action run while a sync creates a collection.</summary>
    public Action? DuringSync { get; set; }

    public Plugin Plugin { get; }

    public ILibraryManager Library { get; } = Substitute.For<ILibraryManager>();

    public ICollectionManager Collections { get; } = Substitute.For<ICollectionManager>();

    public IProviderManager Providers { get; } = Substitute.For<IProviderManager>();

    public TestActivityManager Activity { get; } = new();

    public LocalMovieSetManager Manager { get; }

    public List<Movie> Movies { get; } = [];

    public List<BoxSet> BoxSets { get; } = [];

    /// <summary>Gets the current members of each collection, by collection ID.</summary>
    public Dictionary<Guid, List<Movie>> Members { get; } = [];

    public List<CollectionCreationOptions> Created { get; } = [];

    public List<(Guid Collection, Guid[] Movies)> Added { get; } = [];

    public List<(Guid Collection, Guid[] Movies)> Removed { get; } = [];

    public List<BaseItem> Deleted { get; } = [];

    /// <summary>Gets collection names whose deletion throws.</summary>
    public HashSet<string> FailDeleteFor { get; } = [];

    /// <summary>Gets a value indicating whether anything was created, changed or deleted.</summary>
    public bool HasWrites => Created.Count > 0 || Added.Count > 0 || Removed.Count > 0 || Deleted.Count > 0;

    /// <summary>
    /// Adds a movie with its own folder, video file and NFO.
    /// </summary>
    /// <param name="name">Movie title (also the folder and file name).</param>
    /// <param name="setName">Set name for the NFO's &lt;set&gt;, or <c>null</c> for no set.</param>
    /// <param name="brokenNfo">Write an NFO that is not valid XML.</param>
    /// <param name="year">Release year (sort order inside a collection).</param>
    public Movie AddMovie(string name, string? setName, bool brokenNfo = false, int year = 2000)
    {
        var folder = Directory.CreateDirectory(Path.Combine(LibraryDir, name)).FullName;
        var videoPath = Path.Combine(folder, name + ".mkv");
        File.WriteAllText(videoPath, "video");

        var nfo = brokenNfo
            ? "<movie><title>" + name + "</title><set><name>"
            : "<movie><title>" + name + "</title>"
              + (setName is null ? string.Empty : "<set><name>" + setName + "</name></set>")
              + "</movie>";
        File.WriteAllText(Path.Combine(folder, name + ".nfo"), nfo);

        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = videoPath,
            PremiereDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),

            // Saved "in the future": the NFO is not newer, so no NFO refresh runs.
            DateLastSaved = DateTime.UtcNow.AddDays(1)
        };

        // Set directly: the SortName getter needs Jellyfin's static configuration manager.
        movie.SortName = name;
        Movies.Add(movie);
        return movie;
    }

    /// <summary>
    /// Adds an existing collection with the given members.
    /// </summary>
    public BoxSet AddBoxSet(string name, bool owned, params Movie[] members)
    {
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = name };
        if (owned)
        {
            CollectionOwnership.MarkOwned(boxSet, name);
        }

        BoxSets.Add(boxSet);
        Members[boxSet.Id] = members.ToList();
        return boxSet;
    }

    /// <summary>
    /// Creates a set folder with a set NFO and the given extra files.
    /// </summary>
    public string AddSetFolder(string setName, params string[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(SetDataFolder, setName)).FullName;
        File.WriteAllText(Path.Combine(folder, setName + ".nfo"), "<set><title>" + setName + "</title></set>");
        foreach (var file in files)
        {
            File.WriteAllBytes(Path.Combine(folder, file), [1, 2, 3]);
        }

        return folder;
    }

    public List<Movie> MembersOf(string collectionName)
    {
        var boxSet = BoxSets.Single(b => b.Name == collectionName);
        return Members[boxSet.Id];
    }

    public void Dispose()
    {
        Manager.Dispose();
        BaseItem.LibraryManager = _previousStaticLibraryManager!;
        typeof(Plugin).GetProperty(nameof(Plugin.Instance))!.SetValue(null, null);

        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
            // Best effort; the temp folder is unique per test.
        }
    }

    private List<BaseItem> Query(InternalItemsQuery query)
    {
        if (query.IncludeItemTypes.Contains(BaseItemKind.BoxSet))
        {
            return BoxSets.Cast<BaseItem>().ToList();
        }

        if (query.IncludeItemTypes.Contains(BaseItemKind.Movie))
        {
            if (query.DescendantOfId is { } parentId && parentId != Guid.Empty)
            {
                return Members.TryGetValue(parentId, out var members)
                    ? members.Cast<BaseItem>().ToList()
                    : [];
            }

            return Movies.Cast<BaseItem>().ToList();
        }

        return [];
    }
}
