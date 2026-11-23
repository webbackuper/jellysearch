using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellySearch.Configuration;

/// <summary>JellySearch settings (Dashboard > Plugins > JellySearch).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether JellySearch answers the searches for movies, series, episodes and collections.</summary>
    public bool EnableSearch { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether phrases are understood ("comédies des années 90 avec Jim Carrey").</summary>
    public bool NaturalLanguage { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether typos are forgiven.</summary>
    public bool Typos { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the free text is also searched in the overviews.</summary>
    public bool SearchOverview { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the free text is also searched in the actors, directors and writers.</summary>
    public bool SearchPeople { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the free text is also searched in the genres, tags and studios.</summary>
    public bool SearchTags { get; set; } = true;

    /// <summary>Gets or sets the folder holding one folder per USB drive (the MediaID), as in JellyUSB.</summary>
    public string BasePath { get; set; } = "/mnt/usb/";

    /// <summary>Gets or sets a value indicating whether the IMDb ratings are downloaded every week (IMDb public dataset).</summary>
    public bool DownloadImdbRatings { get; set; } = true;

    /// <summary>Gets or sets the address of the IMDb ratings file.</summary>
    public string ImdbRatingsUrl { get; set; } = "https://datasets.imdbws.com/title.ratings.tsv.gz";

    /// <summary>Gets or sets a value indicating whether the details screen of the Android TV app shows the IMDb and TMDB ratings.</summary>
    public bool TvRatingsLine { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the saved searches are tiles in "My media" of the Android TV app.</summary>
    public bool TvSavedTiles { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the web client gets the JellySearch script.</summary>
    public bool EnableWebEnhancements { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the search page of the web client has the filter panel.</summary>
    public bool WebFilterPanel { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the search page of the web client shows how the search was understood.</summary>
    public bool WebExplain { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the details page of the web client shows the IMDb rating.</summary>
    public bool WebRatingsBadge { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the saved searches are tiles in "My media" of the web client.</summary>
    public bool WebSavedTiles { get; set; } = true;
}
