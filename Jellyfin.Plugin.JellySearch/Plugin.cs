using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySearch.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellySearch;

/// <summary>
/// JellySearch: a search that forgives accents and typos, also looks in overviews, people and tags,
/// understands filters and phrases, and turns saved searches into tiles (web and Android TV).
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Plugin identifier (also in build.yaml).</summary>
    public const string PluginGuid = "8b8ecb0d-b645-48ad-a58a-2e79c932f0c7";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellySearch";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description =>
        "Recherche tolérante (accents, fautes de frappe), dans les résumés, acteurs et tags, avec des filtres combinables (note IMDb ou TMDB, année, durée, genre, MediaID…) et des recherches enregistrées en tuiles, sur le web et la TV.";

    /// <summary>Gets the current plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Gets the current configuration, or defaults when the plugin is not loaded (tests).</summary>
    public static PluginConfiguration CurrentConfiguration => Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>Called by Jellyfin when the plugin is uninstalled: removes what JellySearch stored outside its folder.</summary>
    public override void OnUninstalling()
    {
        UninstallCleaner.Current?.Run(ConfigurationFilePath);
        base.OnUninstalling();
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuSection = "Plugins",
                MenuIcon = "search"
            }
        ];
    }
}
