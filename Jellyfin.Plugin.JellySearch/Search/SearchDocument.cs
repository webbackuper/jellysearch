using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>The kinds of items JellySearch answers for (the others are left to Jellyfin).</summary>
public enum DocKind
{
    /// <summary>A movie.</summary>
    Movie,

    /// <summary>A TV series.</summary>
    Series,

    /// <summary>An episode of a series.</summary>
    Episode,

    /// <summary>A collection (box set).</summary>
    BoxSet
}

/// <summary>What a person did for an item.</summary>
public enum PersonRole
{
    /// <summary>Actor, guest star.</summary>
    Actor,

    /// <summary>Director.</summary>
    Director,

    /// <summary>Writer.</summary>
    Writer,

    /// <summary>Producer, composer and the others.</summary>
    Other
}

/// <summary>A person credited on an item.</summary>
public sealed record DocPerson(string Name, PersonRole Role);

/// <summary>
/// What JellySearch knows about one library item: a copy of the fields it searches and filters on,
/// taken from Jellyfin's database (nothing is written back).
/// </summary>
public sealed class SearchDocument
{
    public Guid Id { get; init; }

    public DocKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? SortName { get; init; }

    public string? OriginalTitle { get; init; }

    /// <summary>For an episode, the name of its series.</summary>
    public string? SeriesName { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Studios { get; init; } = [];

    public IReadOnlyList<DocPerson> People { get; init; } = [];

    public int? Year { get; init; }

    public double? RuntimeMinutes { get; init; }

    /// <summary>Jellyfin's community rating: the TMDB rating with the default providers and Kodi NFO files.</summary>
    public double? CommunityRating { get; init; }

    /// <summary>Jellyfin's critic rating (0-100, Rotten Tomatoes when a provider fills it).</summary>
    public double? CriticRating { get; init; }

    public string? ImdbId { get; init; }

    public string? OfficialRating { get; init; }

    public DateTime DateCreated { get; init; }

    public string? Path { get; init; }

    /// <summary>The MediaID: the folder after the base path of the USB drives (null when not on one).</summary>
    public string? MediaId { get; init; }
}
