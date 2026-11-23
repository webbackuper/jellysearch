using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySearch.Search;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Ratings;

/// <summary>
/// Scheduled task "Télécharger les notes IMDb": downloads IMDb's public ratings file (about 8 MB) once a
/// week and keeps the ratings of the library's IMDb ids. Off when the setting is unticked.
/// </summary>
public sealed class ImdbRatingsTask : IScheduledTask
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ImdbRatings _ratings;
    private readonly LibraryIndex _index;
    private readonly ILogger<ImdbRatingsTask> _logger;

    public ImdbRatingsTask(IHttpClientFactory httpClientFactory, ImdbRatings ratings, LibraryIndex index, ILogger<ImdbRatingsTask> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ratings = ratings;
        _index = index;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Télécharger les notes IMDb";

    /// <inheritdoc />
    public string Key => "JellySearchImdbRatings";

    /// <inheritdoc />
    public string Description => "Télécharge le fichier public des notes IMDb (usage personnel) et garde celles des films et séries de la médiathèque, pour les filtres « imdb » de JellySearch.";

    /// <inheritdoc />
    public string Category => "JellySearch";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(7).Ticks }];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.CurrentConfiguration;
        if (!config.DownloadImdbRatings)
        {
            _logger.LogInformation("JellySearch: téléchargement des notes IMDb désactivé dans les réglages");
            return;
        }

        if (_index.Current.Docs.Count == 0)
        {
            await _index.RebuildAsync().ConfigureAwait(false);
        }

        var wanted = _index.Current.Docs.Select(d => d.ImdbId).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            _logger.LogInformation("JellySearch: aucun identifiant IMDb dans la médiathèque, rien à télécharger");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_ratings.CacheFile)!);
        var tmp = _ratings.CacheFile + ".part";
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using (var response = await client.GetAsync(config.ImdbRatingsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (total is > 0)
                {
                    progress.Report(Math.Min(90, done * 90.0 / total.Value));
                }
            }
        }

        File.Move(tmp, _ratings.CacheFile, true);
        var count = _ratings.Update(_ratings.CacheFile, wanted);
        progress.Report(100);
        _logger.LogInformation("JellySearch: {Count} notes IMDb sur {Wanted} identifiants de la médiathèque", count, wanted.Count);
    }
}
