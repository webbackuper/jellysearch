using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Jellyfin.Plugin.JellySearch.Web;

/// <summary>
/// Answers an items request with items JellySearch chose: the request goes on to Jellyfin with
/// "ids=…" instead of the search (so the answer has every field the client asked for, in Jellyfin's own
/// format, with the user's rights applied), then the items are put back in JellySearch's order.
/// </summary>
public static class IdsForwarder
{
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        "searchTerm", "startIndex", "limit", "sortBy", "sortOrder", "parentId", "ids", "enableTotalRecordCount", "recursive", "nameStartsWith", "nameStartsWithOrGreater", "nameLessThan"
    };

    /// <summary>Answers with the items <paramref name="page"/>, in that order; <paramref name="total"/> is the number of results of every page.</summary>
    public static async Task ForwardAsync(HttpContext context, RequestDelegate next, IReadOnlyList<Guid> page, int total, int start, string? understood)
    {
        if (understood is not null)
        {
            context.Response.Headers["X-JellySearch-Query"] = Uri.EscapeDataString(understood);
        }

        if (page.Count == 0)
        {
            await WriteEmptyAsync(context, total, start).ConfigureAwait(false);
            return;
        }

        var request = context.Request;
        var query = new QueryBuilder();
        foreach (var (key, values) in request.Query)
        {
            if (!Dropped.Contains(key))
            {
                foreach (var v in values)
                {
                    query.Add(key, v ?? string.Empty);
                }
            }
        }

        query.Add("ids", string.Join(',', page.Select(id => id.ToString("N"))));
        query.Add("recursive", "true");
        query.Add("enableTotalRecordCount", "false");
        request.QueryString = query.ToQueryString();

        // a plain, complete copy: no compression, no 304
        request.Headers.Remove("Accept-Encoding");
        request.Headers.Remove("If-None-Match");
        request.Headers.Remove("If-Modified-Since");

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        var bytes = buffer.ToArray();
        if (context.Response.StatusCode == StatusCodes.Status200OK && bytes.Length > 0
            && JsonNode.Parse(bytes) is JsonObject result && result["Items"] is JsonArray items)
        {
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < page.Count; i++)
            {
                order[page[i].ToString("N")] = i;
            }

            var sorted = items
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderBy(n => order.TryGetValue((n["Id"]?.GetValue<string>() ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal), out var o) ? o : int.MaxValue)
                .ToList();
            items.Clear();
            foreach (var n in sorted)
            {
                items.Add(n);
            }

            result["TotalRecordCount"] = total;
            result["StartIndex"] = start;
            bytes = Encoding.UTF8.GetBytes(result.ToJsonString());
        }

        context.Response.ContentLength = bytes.Length;
        await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>An empty page.</summary>
    public static async Task WriteEmptyAsync(HttpContext context, int total, int start)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"Items\":[],\"TotalRecordCount\":" + total + ",\"StartIndex\":" + start + "}");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>A comma list or repeated parameter, as a set.</summary>
    public static HashSet<string> List(IQueryCollection query, string name)
        => new(query[name].SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), StringComparer.OrdinalIgnoreCase);
}
