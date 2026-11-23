using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Ratings;

/// <summary>
/// The IMDb ratings of the library, read from IMDb's public dataset (title.ratings.tsv.gz, for personal
/// and non-commercial use): kept in a small file next to the plugin settings, only for the IMDb ids of
/// the library. Nothing is written to Jellyfin's database.
/// </summary>
public sealed class ImdbRatings
{
    private readonly ILogger<ImdbRatings> _logger;
    private readonly object _lock = new();
    private Dictionary<string, (double Rating, int Votes)> _ratings = new(StringComparer.Ordinal);

    public ImdbRatings(IApplicationPaths paths, ILogger<ImdbRatings> logger)
    {
        _logger = logger;
        Folder = Path.Combine(paths.PluginConfigurationsPath, "JellySearch");
        CacheFile = Path.Combine(paths.CachePath, "jellysearch", "title.ratings.tsv.gz");
        Load();
    }

    /// <summary>The folder of JellySearch's own files (next to the plugin settings).</summary>
    public string Folder { get; }

    /// <summary>The downloaded dataset, kept to read new IMDb ids without downloading it again.</summary>
    public string CacheFile { get; }

    private string RatingsFile => Path.Combine(Folder, "imdb-ratings.json");

    /// <summary>The number of ratings known.</summary>
    public int Count => _ratings.Count;

    /// <summary>When the ratings were last read from the dataset.</summary>
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>The rating and votes of <paramref name="imdbId"/> ("tt0078748").</summary>
    public (double Rating, int Votes)? Get(string? imdbId)
        => imdbId is not null && _ratings.TryGetValue(imdbId, out var r) ? r : null;

    /// <summary>Whether some of <paramref name="imdbIds"/> have no rating yet.</summary>
    public bool Misses(IEnumerable<string> imdbIds) => imdbIds.Any(id => !_ratings.ContainsKey(id));

    /// <summary>Reads the ratings of <paramref name="wanted"/> from the dataset <paramref name="gzipFile"/> and saves them.</summary>
    public int Update(string gzipFile, IReadOnlySet<string> wanted)
    {
        var found = new Dictionary<string, (double, int)>(StringComparer.Ordinal);
        using (var file = File.OpenRead(gzipFile))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
        {
            reader.ReadLine(); // tconst averageRating numVotes
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var tab = line.IndexOf('\t', StringComparison.Ordinal);
                if (tab <= 0 || !wanted.Contains(line[..tab]))
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length >= 3
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
                    && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes))
                {
                    found[parts[0]] = (rating, votes);
                }
            }
        }

        lock (_lock)
        {
            _ratings = found;
            UpdatedAt = DateTime.UtcNow;
            Save();
        }

        return found.Count;
    }

    /// <summary>Forgets everything (uninstall).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _ratings = new(StringComparer.Ordinal);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(RatingsFile))
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(RatingsFile));
            if (stored?.Ratings is not null)
            {
                _ratings = stored.Ratings.ToDictionary(kv => kv.Key, kv => (kv.Value[0], (int)kv.Value[1]), StringComparer.Ordinal);
                UpdatedAt = stored.UpdatedAt;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellySearch: notes IMDb illisibles, elles seront téléchargées à nouveau");
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Folder);
        var stored = new Stored
        {
            UpdatedAt = UpdatedAt,
            Ratings = _ratings.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.Rating, kv.Value.Votes })
        };
        var tmp = RatingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stored));
        File.Move(tmp, RatingsFile, true);
    }

    private sealed class Stored
    {
        public DateTime? UpdatedAt { get; set; }

        public Dictionary<string, double[]>? Ratings { get; set; }
    }
}
