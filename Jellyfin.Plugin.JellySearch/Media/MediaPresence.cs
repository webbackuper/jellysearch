using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.JellySearch.Media;

/// <summary>
/// The MediaID of a file (the folder after the base path of the USB drives, as in JellyUSB) and whether
/// its drive is plugged in. An automount point with nothing mounted is never touched (it would hang).
/// </summary>
public class MediaPresence
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(10);
    private readonly MountTable _mountTable;
    private readonly ConcurrentDictionary<string, (DateTime At, bool Present)> _cache = new(StringComparer.Ordinal);

    public MediaPresence(MountTable mountTable)
    {
        _mountTable = mountTable;
    }

    /// <summary>The MediaID of <paramref name="path"/>: the first folder below <paramref name="basePath"/>.</summary>
    public static string? GetMediaId(string? path, string? basePath)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(basePath))
        {
            return null;
        }

        var prefix = basePath.TrimEnd('/') + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path.AsSpan(prefix.Length);
        var slash = rest.IndexOf('/');
        var mediaId = slash < 0 ? rest.ToString() : rest[..slash].ToString();
        return mediaId.Length == 0 || mediaId == "." || mediaId == ".." ? null : mediaId;
    }

    /// <summary>Whether the drive <paramref name="mediaId"/> under <paramref name="basePath"/> is plugged in (cached 10 s).</summary>
    public bool IsPresent(string basePath, string mediaId)
    {
        var root = basePath.TrimEnd('/') + "/" + mediaId;
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(root, out var cached) && now - cached.At < CacheTime)
        {
            return cached.Present;
        }

        var present = _mountTable.GetState(root) switch
        {
            MountState.Mounted => true,
            MountState.UnmountedAutomount => false,
            _ => HasEntries(root)
        };
        _cache[root] = (now, present);
        return present;
    }

    private static bool HasEntries(string root)
    {
        try
        {
            return Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
