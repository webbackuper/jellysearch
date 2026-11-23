using System;
using Jellyfin.Plugin.JellySearch.Media;
using Jellyfin.Plugin.JellySearch.Ratings;
using Jellyfin.Plugin.JellySearch.Saved;
using Jellyfin.Plugin.JellySearch.Search;
using Jellyfin.Plugin.JellySearch.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellySearch;

/// <summary>Registers the JellySearch services.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MountTable>();
        serviceCollection.AddSingleton<MediaPresence>();
        serviceCollection.AddSingleton<LibraryIndex>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<LibraryIndex>());
        serviceCollection.AddSingleton<ImdbRatings>();
        serviceCollection.AddSingleton<SearchService>();
        serviceCollection.AddSingleton<SavedSearchStore>();
        serviceCollection.AddSingleton<UninstallCleaner>();
        serviceCollection.AddHostedService<UninstallCleaner.Starter>();
        serviceCollection.AddTransient<IStartupFilter, JellySearchStartupFilter>();
    }
}

/// <summary>Inserts the JellySearch middlewares at the start of the HTTP pipeline.</summary>
public class JellySearchStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebClientInjectionMiddleware>();
            app.UseMiddleware<TvSavedViewsMiddleware>();
            app.UseMiddleware<SearchMiddleware>();
            app.UseMiddleware<TvRatingsMiddleware>();
            next(app);
        };
    }
}
