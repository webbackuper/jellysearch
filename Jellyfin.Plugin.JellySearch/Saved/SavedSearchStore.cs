using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Saved;

/// <summary>A saved search: a name and a query, shown as a tile in "My media".</summary>
public sealed record SavedSearch
{
    public Guid Id { get; init; }

    /// <summary>The user who saved it: only they see the tile.</summary>
    public Guid UserId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>The query, in the query language ("genre:horreur annee:1980..1989").</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>How the query was understood, in plain French (for the tile image).</summary>
    public string Description { get; init; } = string.Empty;

    public DateTime Created { get; init; }

    public int Order { get; init; }
}

/// <summary>
/// The saved searches of every user, in a small JSON file next to the plugin settings (removed with the
/// plugin). Nothing is written to Jellyfin's database: the tiles exist only in the answers to the clients.
/// </summary>
public sealed class SavedSearchStore
{
    private readonly object _lock = new();
    private readonly ILogger<SavedSearchStore> _logger;
    private List<SavedSearch> _all = [];

    public SavedSearchStore(IApplicationPaths paths, ILogger<SavedSearchStore> logger)
    {
        _logger = logger;
        Folder = Path.Combine(paths.PluginConfigurationsPath, "JellySearch");
        Load();
    }

    /// <summary>The folder of JellySearch's own files.</summary>
    public string Folder { get; }

    private string FilePath => Path.Combine(Folder, "saved-searches.json");

    /// <summary>The number of saved searches of every user.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _all.Count;
            }
        }
    }

    /// <summary>The saved searches of <paramref name="userId"/>, in their order.</summary>
    public IReadOnlyList<SavedSearch> ForUser(Guid userId)
    {
        lock (_lock)
        {
            return _all.Where(s => s.UserId == userId).OrderBy(s => s.Order).ThenBy(s => s.Created).ToList();
        }
    }

    /// <summary>The saved search <paramref name="id"/>, whoever saved it.</summary>
    public SavedSearch? Get(Guid id)
    {
        lock (_lock)
        {
            return _all.FirstOrDefault(s => s.Id == id);
        }
    }

    /// <summary>Saves a new search for <paramref name="userId"/>.</summary>
    public SavedSearch Add(Guid userId, string name, string query, string description)
    {
        lock (_lock)
        {
            var saved = new SavedSearch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = Clean(name, 60),
                Query = Clean(query, 500),
                Description = Clean(description, 300),
                Created = DateTime.UtcNow,
                Order = _all.Where(s => s.UserId == userId).Select(s => s.Order + 1).DefaultIfEmpty(0).Max()
            };
            _all.Add(saved);
            Save();
            return saved;
        }
    }

    /// <summary>Renames or changes the query of a search of <paramref name="userId"/>; null when it is not theirs.</summary>
    public SavedSearch? Update(Guid userId, Guid id, string? name, string? query, string? description)
    {
        lock (_lock)
        {
            var index = _all.FindIndex(s => s.Id == id && s.UserId == userId);
            if (index < 0)
            {
                return null;
            }

            var s = _all[index];
            _all[index] = s with
            {
                Name = name is null ? s.Name : Clean(name, 60),
                Query = query is null ? s.Query : Clean(query, 500),
                Description = description is null ? s.Description : Clean(description, 300)
            };
            Save();
            return _all[index];
        }
    }

    /// <summary>Deletes a search of <paramref name="userId"/>.</summary>
    public bool Delete(Guid userId, Guid id)
    {
        lock (_lock)
        {
            var removed = _all.RemoveAll(s => s.Id == id && s.UserId == userId) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    private static string Clean(string value, int max)
    {
        var text = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                _all = JsonSerializer.Deserialize<List<SavedSearch>>(File.ReadAllText(FilePath)) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellySearch: recherches enregistrées illisibles ({File})", FilePath);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
    }
}
