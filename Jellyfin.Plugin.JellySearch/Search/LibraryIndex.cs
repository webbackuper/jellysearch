using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellySearch.Media;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>
/// Keeps the <see cref="SearchIndex"/> of the library: built in the background after startup, rebuilt a
/// few seconds after the library changes (a scan adds many items: one rebuild at the end). Only reads
/// the database; the drives are never touched.
/// </summary>
public sealed class LibraryIndex : IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(15);
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryIndex> _logger;
    private readonly ConcurrentDictionary<Guid, DocPerson[]> _people = new();
    private readonly SemaphoreSlim _building = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private Timer? _timer;
    private volatile SearchIndex _index = SearchIndex.Empty;
    private int _version;

    public LibraryIndex(ILibraryManager libraryManager, ILogger<LibraryIndex> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>The index to search in.</summary>
    public SearchIndex Current => _index;

    /// <summary>Changes with each rebuild (cache key).</summary>
    public int Version => _version;

    /// <summary>Raised after a rebuild.</summary>
    public event EventHandler? Rebuilt;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        _timer = new Timer(_ => _ = RebuildAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);

        // the MediaID depends on the base path of the drives: settings saved, index rebuilt
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += (_, _) => _timer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        _stopping.Cancel();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _building.Dispose();
        _stopping.Dispose();
    }

    /// <summary>Rebuilds now (and waits for it).</summary>
    public async Task RebuildAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        await _building.WaitAsync().ConfigureAwait(false);
        try
        {
            var watch = Stopwatch.StartNew();
            var basePath = Plugin.CurrentConfiguration.BasePath;
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode, BaseItemKind.BoxSet],
                Recursive = true,
                IsVirtualItem = false
            });
            var docs = items.Select(i => ToDocument(i, basePath)).Where(d => d is not null).Cast<SearchDocument>().ToList();
            var index = SearchIndex.Build(docs);
            _index = index;
            Interlocked.Increment(ref _version);
            _logger.LogInformation("JellySearch: index de {Count} éléments construit en {Ms} ms", docs.Count, watch.ElapsedMilliseconds);
            Rebuilt?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellySearch: index non construit");
        }
        finally
        {
            _building.Release();
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not (Movie or Series or Episode or BoxSet))
        {
            return;
        }

        _people.TryRemove(e.Item.Id, out _);
        _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private SearchDocument? ToDocument(BaseItem item, string basePath)
    {
        var kind = item switch
        {
            Movie => DocKind.Movie,
            Series => DocKind.Series,
            Episode => DocKind.Episode,
            BoxSet => (DocKind?)DocKind.BoxSet,
            _ => null
        };
        if (kind is null || string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        // the people of episodes are not read (thousands of guest stars, slow to load)
        var people = kind == DocKind.Episode ? [] : _people.GetOrAdd(item.Id, _ => ReadPeople(item));
        return new SearchDocument
        {
            Id = item.Id,
            Kind = kind.Value,
            Name = item.Name,
            SortName = item.SortName,
            OriginalTitle = item.OriginalTitle,
            SeriesName = (item as Episode)?.SeriesName,
            Overview = item.Overview,
            Genres = item.Genres ?? [],
            Tags = item.Tags ?? [],
            Studios = item.Studios ?? [],
            People = people,
            Year = item.ProductionYear,
            RuntimeMinutes = item.RunTimeTicks is { } ticks && ticks > 0 ? Math.Round(TimeSpan.FromTicks(ticks).TotalMinutes, 1) : null,
            // ratings are stored as float: 7.8f is 7.80000019 as a double, so "note>7.8" would keep it
            CommunityRating = item.CommunityRating is { } c ? Math.Round((double)c, 2) : null,
            CriticRating = item.CriticRating is { } cr ? Math.Round((double)cr, 2) : null,
            ImdbId = item.GetProviderId(MetadataProvider.Imdb),
            OfficialRating = item.OfficialRating,
            DateCreated = item.DateCreated,
            Path = item.Path,
            MediaId = MediaPresence.GetMediaId(item.Path, basePath)
        };
    }

    private DocPerson[] ReadPeople(BaseItem item)
    {
        try
        {
            return _libraryManager.GetPeople(item)
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new DocPerson(p.Name, p.Type switch
                {
                    PersonKind.Actor or PersonKind.GuestStar => PersonRole.Actor,
                    PersonKind.Director => PersonRole.Director,
                    PersonKind.Writer => PersonRole.Writer,
                    _ => PersonRole.Other
                }))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "JellySearch: people of {Item} not read", item.Id);
            return [];
        }
    }
}
