using Jellyfin.Plugin.LocalMovieSets.Parsers;
using Jellyfin.Plugin.LocalMovieSets.ScheduledTasks;
using Jellyfin.Plugin.LocalMovieSets.Services;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LocalMovieSets;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// Jellyfin auto-discovers this class via reflection on plugin load.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Parsers — stateless, singleton is appropriate
        serviceCollection.AddSingleton<MovieNfoParser>();
        serviceCollection.AddSingleton<SetNfoParser>();

        // Note: BoxSetMetadataProvider and BoxSetImageProvider are NOT registered
        // here. Jellyfin discovers IMetadataProvider/IImageProvider implementations
        // in plugin assemblies automatically and instantiates them with DI.

        // Core manager — implements IHostedService to listen for library events
        serviceCollection.AddSingleton<LocalMovieSetManager>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<LocalMovieSetManager>());

        // Scheduled task — appears in Jellyfin Dashboard under Scheduled Tasks
        serviceCollection.AddSingleton<IScheduledTask, SyncMovieSetsTask>();
    }
}
