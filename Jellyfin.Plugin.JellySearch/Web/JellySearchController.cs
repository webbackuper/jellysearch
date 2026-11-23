using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySearch.Ratings;
using Jellyfin.Plugin.JellySearch.Saved;
using Jellyfin.Plugin.JellySearch.Search;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellySearch.Web;

/// <summary>How a search is understood.</summary>
public sealed record Explanation(string Syntax, string Description, bool NaturalLanguage, bool HasText);

/// <summary>A field of the query language, for the filter panel.</summary>
public sealed record FieldHelp(string Syntax, string Label, string Kind, IReadOnlyList<string> Names);

/// <summary>What the filter panel offers: the fields and the values found in the library.</summary>
public sealed record Facets(IReadOnlyList<FieldHelp> Fields, IReadOnlyList<string> Genres, IReadOnlyList<string> Tags, IReadOnlyList<string> Studios, IReadOnlyList<string> MediaIds, int? MinYear, int? MaxYear, bool ImdbRatings);

/// <summary>The ratings of an item.</summary>
public sealed record ItemRatings(string? ImdbId, double? Imdb, int? ImdbVotes, double? Tmdb, double? Critic);

/// <summary>A saved search, as the web page creates or changes it.</summary>
public sealed record SavedSearchRequest(string? Name, string? Query);

/// <summary>The state of JellySearch, for the settings page.</summary>
public sealed record Status(int Indexed, int ImdbRatings, DateTime? ImdbUpdatedAt, int SavedSearches);

/// <summary>JellySearch endpoints used by the web script and the settings page.</summary>
[ApiController]
[Route("JellySearch")]
public class JellySearchController : ControllerBase
{
    private readonly SearchService _search;
    private readonly SavedSearchStore _saved;
    private readonly ImdbRatings _ratings;
    private readonly ILibraryManager _libraryManager;
    private readonly IAuthorizationContext _authContext;

    public JellySearchController(SearchService search, SavedSearchStore saved, ImdbRatings ratings, ILibraryManager libraryManager, IAuthorizationContext authContext)
    {
        _search = search;
        _saved = saved;
        _ratings = ratings;
        _libraryManager = libraryManager;
        _authContext = authContext;
    }

    /// <summary>The script added to jellyfin-web (no secret inside: the settings of the web features).</summary>
    [HttpGet("web.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public IActionResult GetScript()
    {
        var config = Plugin.CurrentConfiguration;
        using var stream = typeof(JellySearchController).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellySearch.Web.jellysearch.js");
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        var settings = JsonSerializer.Serialize(new
        {
            search = config.EnableSearch,
            filterPanel = config.WebFilterPanel,
            explain = config.WebExplain,
            ratingsBadge = config.WebRatingsBadge,
            savedTiles = config.WebSavedTiles,
            version = typeof(JellySearchController).Assembly.GetName().Version?.ToString()
        });
        var script = reader.ReadToEnd().Replace("/*JELLYSEARCH_SETTINGS*/null", settings, StringComparison.Ordinal);
        Response.Headers.CacheControl = "no-cache";
        return Content(script, "application/javascript; charset=utf-8");
    }

    /// <summary>How <paramref name="q"/> is understood: the query language and plain French.</summary>
    [HttpGet("Explain")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Explanation> Explain([FromQuery] string? q)
    {
        var parsed = _search.Explain(q ?? string.Empty);
        return new Explanation(parsed.Syntax, parsed.Description, parsed.NaturalLanguage, parsed.HasText);
    }

    /// <summary>The fields of the query language and the values of the library, for the filter panel.</summary>
    [HttpGet("Facets")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Facets> GetFacets()
    {
        var index = _search.Index;
        var years = index.Docs.Where(d => d.Year is > 1800).Select(d => d.Year!.Value).ToList();
        static List<string> Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new Facets(
            Fields.All.Select(f => new FieldHelp(f.Syntax, f.Label, f.Kind.ToString(), f.Names)).ToList(),
            Sorted(index.Genres.Values),
            Sorted(index.Tags.Values),
            Sorted(index.Studios.Values),
            Sorted(index.MediaIds.Values),
            years.Count > 0 ? years.Min() : null,
            years.Count > 0 ? years.Max() : null,
            _ratings.Count > 0);
    }

    /// <summary>The IMDb, TMDB and critic ratings of an item (204 when there are none).</summary>
    [HttpGet("Items/{itemId}/Ratings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemRatings>> GetRatings([FromRoute] Guid itemId)
    {
        if (await CurrentUserAsync().ConfigureAwait(false) is not { } user)
        {
            return Unauthorized();
        }

        if (_libraryManager.GetItemById(itemId) is not { } item || !item.IsVisible(user))
        {
            return NotFound();
        }

        var imdbId = item.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb);
        var imdb = _ratings.Get(imdbId);
        if (imdb is null && item.CommunityRating is null && item.CriticRating is null)
        {
            return NoContent();
        }

        return new ItemRatings(imdbId, imdb?.Rating, imdb?.Votes, item.CommunityRating, item.CriticRating);
    }

    /// <summary>The saved searches of the current user.</summary>
    [HttpGet("Saved")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SavedSearch>>> GetSaved()
    {
        if (await CurrentUserAsync().ConfigureAwait(false) is not { } user)
        {
            return Unauthorized();
        }

        return Ok(_saved.ForUser(user.Id));
    }

    /// <summary>Saves a search of the current user (it becomes a tile in "My media").</summary>
    [HttpPost("Saved")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavedSearch>> AddSaved([FromBody] SavedSearchRequest body)
    {
        if (await CurrentUserAsync().ConfigureAwait(false) is not { } user)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(body.Query) || string.IsNullOrWhiteSpace(body.Name))
        {
            return BadRequest("Nom et recherche obligatoires");
        }

        var parsed = _search.Explain(body.Query);
        var query = string.IsNullOrWhiteSpace(parsed.Syntax) ? body.Query : parsed.Syntax;
        return Ok(_saved.Add(user.Id, body.Name, query, parsed.Description));
    }

    /// <summary>Renames a saved search, or changes its query.</summary>
    [HttpPost("Saved/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavedSearch>> UpdateSaved([FromRoute] Guid id, [FromBody] SavedSearchRequest body)
    {
        if (await CurrentUserAsync().ConfigureAwait(false) is not { } user)
        {
            return Unauthorized();
        }

        string? query = null, description = null;
        if (!string.IsNullOrWhiteSpace(body.Query))
        {
            var parsed = _search.Explain(body.Query);
            query = string.IsNullOrWhiteSpace(parsed.Syntax) ? body.Query : parsed.Syntax;
            description = parsed.Description;
        }

        var updated = _saved.Update(user.Id, id, string.IsNullOrWhiteSpace(body.Name) ? null : body.Name, query, description);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Deletes a saved search of the current user.</summary>
    [HttpDelete("Saved/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSaved([FromRoute] Guid id)
    {
        if (await CurrentUserAsync().ConfigureAwait(false) is not { } user)
        {
            return Unauthorized();
        }

        return _saved.Delete(user.Id, id) ? NoContent() : NotFound();
    }

    /// <summary>The image of a saved search tile (anonymous, like Jellyfin's images: it shows the name only).</summary>
    [HttpGet("Saved/{id}/Image")]
    [AllowAnonymous]
    [Produces("image/png")]
    public IActionResult GetSavedImage([FromRoute] Guid id, [FromQuery] int? width)
    {
        if (_saved.Get(id) is not { } saved)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(TileImage.Png(saved.Name, saved.Description, width ?? 960), "image/png");
    }

    /// <summary>The plugin image.</summary>
    [HttpGet("Image")]
    [AllowAnonymous]
    [Produces("image/png")]
    public IActionResult GetPluginImage()
    {
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(TileImage.PluginPng(), "image/png");
    }

    /// <summary>The state of the index and the IMDb ratings (administrators).</summary>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Status> GetStatus()
        => new Status(_search.Index.Docs.Count, _ratings.Count, _ratings.UpdatedAt, _saved.Count);

    private async Task<User?> CurrentUserAsync()
        => (await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false)).User;
}
