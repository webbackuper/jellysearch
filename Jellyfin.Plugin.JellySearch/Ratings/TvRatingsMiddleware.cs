using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySearch.Media;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Ratings;

/// <summary>
/// Shows the IMDb and TMDB ratings on the details screen of the Android TV app: a first line is added to
/// the overview in the answer to GET /Items/{id} ("⭐ IMDb **8,5** · TMDB **8,1**"; the app renders the
/// overview as markdown). Only when the IMDb rating is known; nothing is stored.
/// </summary>
public partial class TvRatingsMiddleware
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}";
    private readonly RequestDelegate _next;
    private readonly ILogger<TvRatingsMiddleware> _logger;

    public TvRatingsMiddleware(RequestDelegate next, ILogger<TvRatingsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    [GeneratedRegex("/(?:Users/" + GuidPattern + "/)?Items/(?<id>" + GuidPattern + ")/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemRoute();

    public async Task InvokeAsync(HttpContext context, ILibraryManager libraryManager, IAuthorizationContext authorizationContext, ImdbRatings ratings)
    {
        var request = context.Request;
        var match = Plugin.CurrentConfiguration.TvRatingsLine && HttpMethods.IsGet(request.Method) && request.Path.Value is { } path
            ? ItemRoute().Match(path)
            : Match.Empty;
        if (!match.Success
            || !Guid.TryParse(match.Groups["id"].Value, out var itemId)
            || libraryManager.GetItemById(itemId) is not { } item
            || ratings.Get(item.GetProviderId(MetadataProvider.Imdb)) is not { } imdb)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var auth = await authorizationContext.GetAuthorizationInfo(context).ConfigureAwait(false);
        if (!auth.IsAuthenticated || ClientClassifier.Classify(auth.Client) != ClientKind.AndroidTv)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        request.Headers.Remove("Accept-Encoding");
        request.Headers.Remove("If-None-Match");
        request.Headers.Remove("If-Modified-Since");
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        var bytes = buffer.ToArray();
        if (context.Response.StatusCode == StatusCodes.Status200OK
            && context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var line = Line(imdb.Rating, imdb.Votes, item.CommunityRating);
                if (AddToOverview(Encoding.UTF8.GetString(bytes), line) is { } decorated)
                {
                    bytes = Encoding.UTF8.GetBytes(decorated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "JellySearch: notes non ajoutées à la fiche de {Item}", item.Id);
            }
        }

        context.Response.ContentLength = bytes.Length;
        await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The line of the ratings (French decimal comma, votes in thousands).</summary>
    public static string Line(double imdb, int votes, double? tmdb)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var votesText = votes >= 1000 ? (votes / 1000d).ToString(votes >= 100_000 ? "0" : "0.#", fr) + " k votes" : votes.ToString(fr) + " votes";
        var line = "⭐ IMDb **" + imdb.ToString("0.0", fr) + "** (" + votesText + ")";
        if (tmdb is { } t && t > 0)
        {
            line += " · TMDB **" + t.ToString("0.0", fr) + "**";
        }

        return line;
    }

    /// <summary>Puts <paramref name="line"/> before the overview of the item in <paramref name="json"/>; null when it is not an item.</summary>
    public static string? AddToOverview(string json, string line)
    {
        if (JsonNode.Parse(json) is not JsonObject dto || dto["Id"] is null)
        {
            return null;
        }

        var overview = dto["Overview"]?.GetValue<string>();
        dto["Overview"] = string.IsNullOrWhiteSpace(overview) ? line : line + "\n\n" + overview;
        return dto.ToJsonString();
    }
}
