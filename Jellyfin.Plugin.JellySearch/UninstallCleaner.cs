using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySearch.Ratings;
using Jellyfin.Plugin.JellySearch.Search;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch;

/// <summary>
/// Undoes, when JellySearch is uninstalled from Jellyfin, what it stored outside its own folder (which
/// Jellyfin deletes itself): its settings file, its folder next to the settings (saved searches, IMDb
/// ratings), its cache folder (IMDb dataset), the file of its scheduled task and its plugin repository
/// in the server settings (install.sh declares it). JellySearch never
/// writes to Jellyfin's database or to jellyfin-web's files: nothing else is left.
/// </summary>
public class UninstallCleaner
{
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<UninstallCleaner> _logger;

    public UninstallCleaner(IServerConfigurationManager serverConfig, ITaskManager taskManager, ILogger<UninstallCleaner> logger)
    {
        _serverConfig = serverConfig;
        _taskManager = taskManager;
        _logger = logger;
        Current = this;
    }

    /// <summary>Gets the instance created at startup.</summary>
    public static UninstallCleaner? Current { get; private set; }

    /// <summary>Cleans up; each step is independent and failures are only logged.</summary>
    public IReadOnlyList<string> Run(string? pluginConfigurationFile)
    {
        var done = new List<string>();
        var paths = _serverConfig.ApplicationPaths;
        Step(done, "recherches enregistrées et notes IMDb supprimées", () => DeleteDirectory(Path.Combine(paths.PluginConfigurationsPath, "JellySearch")));
        Step(done, "dossier cache supprimé", () => DeleteDirectory(Path.Combine(paths.CachePath, "jellysearch")));
        Step(done, "fichiers de la tâche planifiée supprimés", RemoveTaskFiles);
        Step(done, "dépôt de plugins JellySearch retiré", RemoveRepository);
        Step(done, "réglages du plugin supprimés", () => DeleteFile(pluginConfigurationFile));
        _logger.LogInformation("JellySearch: désinstallation, nettoyage : {Done}", done.Count == 0 ? "rien à faire" : string.Join(" ; ", done));
        return done;
    }

    /// <summary>The repositories without the JellySearch one (install.sh declares it).</summary>
    public static MediaBrowser.Model.Updates.RepositoryInfo[] WithoutJellySearch(IEnumerable<MediaBrowser.Model.Updates.RepositoryInfo> repositories)
        => repositories.Where(r => r.Url?.Contains("webbackuper/jellysearch", StringComparison.OrdinalIgnoreCase) != true).ToArray();

    private bool RemoveRepository()
    {
        var config = _serverConfig.Configuration;
        var kept = WithoutJellySearch(config.PluginRepositories);
        if (kept.Length == config.PluginRepositories.Length)
        {
            return false;
        }

        config.PluginRepositories = kept;
        _serverConfig.SaveConfiguration();
        return true;
    }

    private bool RemoveTaskFiles()
    {
        var worker = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask is ImdbRatingsTask);
        if (worker is null)
        {
            return false;
        }

        var name = new Guid(worker.Id) + ".js";
        var paths = _serverConfig.ApplicationPaths;
        var removed = DeleteFile(Path.Combine(paths.ConfigurationDirectoryPath, "ScheduledTasks", name));
        return DeleteFile(Path.Combine(paths.DataPath, "ScheduledTasks", name)) || removed;
    }

    private static bool DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        Directory.Delete(path, true);
        return true;
    }

    private static bool DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private void Step(List<string> done, string label, Func<bool> action)
    {
        try
        {
            if (action())
            {
                done.Add(label);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellySearch: désinstallation, étape non faite : {Step}", label);
        }
    }

    /// <summary>
    /// Created at startup: makes the cleaner ready for an uninstall, and fetches the IMDb ratings the
    /// first time (or reads the new IMDb ids from the dataset already downloaded) once the index is built.
    /// </summary>
    public sealed class Starter : IHostedService
    {
        private readonly LibraryIndex _index;
        private readonly ImdbRatings _ratings;
        private readonly ITaskManager _taskManager;
        private readonly ILogger<Starter> _logger;
        private int _busy;

        public Starter(UninstallCleaner cleaner, LibraryIndex index, ImdbRatings ratings, ITaskManager taskManager, ILogger<Starter> logger)
        {
            _ = cleaner;
            _index = index;
            _ratings = ratings;
            _taskManager = taskManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _index.Rebuilt += OnRebuilt;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _index.Rebuilt -= OnRebuilt;
            return Task.CompletedTask;
        }

        private void OnRebuilt(object? sender, EventArgs e)
        {
            if (!Plugin.CurrentConfiguration.DownloadImdbRatings || Interlocked.Exchange(ref _busy, 1) == 1)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var ids = _index.Current.Docs.Select(d => d.ImdbId).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToHashSet(StringComparer.Ordinal);
                    if (ids.Count == 0 || !_ratings.Misses(ids))
                    {
                        return;
                    }

                    if (File.Exists(_ratings.CacheFile))
                    {
                        _ratings.Update(_ratings.CacheFile, ids);
                    }
                    else if (_ratings.UpdatedAt is null)
                    {
                        _logger.LogInformation("JellySearch: premier téléchargement des notes IMDb");
                        _taskManager.QueueIfNotRunning<ImdbRatingsTask>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "JellySearch: notes IMDb non mises à jour");
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }
    }
}
