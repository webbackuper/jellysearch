using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace Jellyfin.Plugin.JellySearch.Saved;

/// <summary>
/// The image of a saved search tile (16:9): a magnifier over a strip of posters, the name, and the
/// filters underneath, in a colour derived from the name. SVG, turned into PNG for the TV app.
/// </summary>
public static class TileImage
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>The tile as SVG.</summary>
    public static string Svg(string name, string description)
    {
        var hue = Hue(name);
        var dark = Hsl(hue, 0.55, 0.14);
        var mid = Hsl(hue, 0.60, 0.32);
        var accent = Hsl(hue, 0.85, 0.62);
        var title = Escape(Shorten(name, 26));
        var lines = Wrap(description, 52, 2).Select(Escape).ToArray();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""
<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{dark}"/>
      <stop offset="1" stop-color="{mid}"/>
    </linearGradient>
    <linearGradient id="jf" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="#aa5cc3"/>
      <stop offset="1" stop-color="#00a4dc"/>
    </linearGradient>
  </defs>
  <rect width="1280" height="720" fill="url(#bg)"/>
""");

        // a strip of posters, tilted, behind the magnifier
        for (var i = 0; i < 6; i++)
        {
            var x = 560 + (i * 120);
            var y = 70 + ((i % 2) * 40);
            var opacity = 0.18 + (0.07 * (i % 3));
            sb.Append(CultureInfo.InvariantCulture, $"""  <rect x="{x}" y="{y}" width="100" height="150" rx="10" fill="#ffffff" fill-opacity="{opacity:0.00}" transform="rotate(-8 {x + 50} {y + 75})"/>""").Append('\n');
        }

        sb.Append(CultureInfo.InvariantCulture, $"""
  <circle cx="1010" cy="300" r="130" fill="none" stroke="{accent}" stroke-width="34"/>
  <circle cx="1010" cy="300" r="113" fill="#ffffff" fill-opacity="0.10"/>
  <line x1="1105" y1="395" x2="1215" y2="505" stroke="{accent}" stroke-width="46" stroke-linecap="round"/>
  <rect x="80" y="420" width="140" height="10" rx="5" fill="url(#jf)"/>
  <text x="80" y="530" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="84" font-weight="bold" fill="#ffffff">{title}</text>
""");
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""  <text x="82" y="{600 + (i * 52)}" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="40" fill="#ffffff" fill-opacity="0.82">{lines[i]}</text>""").Append('\n');
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The card of the results of a TV search (16:9, the shape the TV app gives to libraries): the number of
    /// results, what was understood, the order, and "Tout voir".
    /// </summary>
    public static string ResultsSvg(int count, string description, string sortLabel)
    {
        var hue = Hue(description.Length > 0 ? description : "jellysearch");
        var dark = Hsl(hue, 0.50, 0.13);
        var mid = Hsl(hue, 0.55, 0.30);
        var accent = Hsl(hue, 0.85, 0.64);
        var number = count.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")).Replace('\u202f', ' ').Replace('\u00a0', ' ');
        var size = number.Length <= 3 ? 190 : number.Length <= 5 ? 150 : 120;
        var wordX = 56 + (int)(number.Length * size * 0.66) + 24;
        var lines = Wrap(description, 34, 2).Select(Escape).ToArray();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""
<svg xmlns="http://www.w3.org/2000/svg" width="800" height="450" viewBox="0 0 800 450">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{mid}"/>
      <stop offset="1" stop-color="{dark}"/>
    </linearGradient>
    <linearGradient id="jf" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="#aa5cc3"/>
      <stop offset="1" stop-color="#00a4dc"/>
    </linearGradient>
  </defs>
  <rect width="800" height="450" fill="url(#bg)"/>
  <circle cx="705" cy="78" r="38" fill="none" stroke="{accent}" stroke-width="11"/>
  <line x1="733" y1="106" x2="764" y2="137" stroke="{accent}" stroke-width="15" stroke-linecap="round"/>
  <text x="50" y="205" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="{size}" font-weight="bold" fill="#ffffff">{Escape(number)}</text>
  <text x="{wordX}" y="205" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="58" fill="#ffffff">{(count == 1 ? "résultat" : "résultats")}</text>
  <rect x="56" y="236" width="110" height="8" rx="4" fill="url(#jf)"/>
""");
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""  <text x="56" y="{292 + (i * 42)}" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="34" fill="#ffffff" fill-opacity="0.88">{lines[i]}</text>""").Append('\n');
        }

        sb.Append(CultureInfo.InvariantCulture, $"""
  <text x="56" y="412" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="28" fill="#ffffff" fill-opacity="0.72">{Escape(Shorten("Tri : " + sortLabel, 30))}</text>
  <rect x="520" y="370" width="240" height="56" rx="28" fill="{accent}"/>
  <text x="640" y="408" text-anchor="middle" font-family="DejaVu Sans, Noto Sans, Arial, sans-serif" font-size="31" font-weight="bold" fill="#10131a">Tout voir  ›</text>
</svg>
""");
        return sb.ToString();
    }

    /// <summary>The card of the results of a TV search as PNG, <paramref name="width"/> pixels wide.</summary>
    public static byte[] ResultsPng(int count, string description, string sortLabel, int width = 800)
    {
        width = Math.Clamp(width, 160, 1920);
        if (Cache.Count > 400)
        {
            Cache.Clear();
        }

        return Cache.GetOrAdd("\u0003" + count + "\u0001" + description + "\u0001" + sortLabel + "\u0001" + width, _ => Render(ResultsSvg(count, description, sortLabel), width));
    }

    /// <summary>The tile as PNG, <paramref name="width"/> pixels wide.</summary>
    public static byte[] Png(string name, string description, int width = 960)
    {
        width = Math.Clamp(width, 160, 1920);
        return Cache.GetOrAdd(name + "\u0001" + description + "\u0001" + width, _ => Render(Svg(name, description), width));
    }

    /// <summary>The plugin image (assets/jellysearch.png).</summary>
    public static byte[] PluginPng()
        => Cache.GetOrAdd("\u0002plugin", _ =>
        {
            using var stream = typeof(TileImage).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellySearch.jellysearch.png")
                ?? throw new InvalidOperationException("plugin image missing");
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });

    internal static int Hue(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()));
        return (hash[0] * 360) / 256;
    }

    internal static byte[] Render(string svgText, int width)
    {
        using var svg = new SKSvg();
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(svgText)))
        {
            svg.Load(input);
        }

        var picture = svg.Picture ?? throw new InvalidOperationException("SVG not rendered");
        var bounds = picture.CullRect;
        var scale = width / bounds.Width;
        var height = (int)Math.Round(bounds.Height * scale);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>An HSL colour as #rrggbb (hue in degrees, saturation and lightness 0-1).</summary>
    internal static string Hsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = c * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = lightness - (c / 2);
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Round((r + m) * 255):x2}{(int)Math.Round((g + m) * 255):x2}{(int)Math.Round((b + m) * 255):x2}");
    }

    private static string Shorten(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static string[] Wrap(string text, int width, int maxLines)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new System.Collections.Generic.List<string>();
        var current = new StringBuilder();
        foreach (var w in words)
        {
            if (current.Length > 0 && current.Length + 1 + w.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                if (lines.Count == maxLines)
                {
                    break;
                }
            }

            current.Append(current.Length > 0 ? " " : string.Empty).Append(w);
        }

        if (lines.Count < maxLines && current.Length > 0)
        {
            lines.Add(current.ToString());
        }
        else if (lines.Count == maxLines && words.Length > 0 && string.Join(' ', lines).Length < string.Join(' ', words).Length)
        {
            lines[^1] = Shorten(lines[^1], width - 1) + (lines[^1].EndsWith('…') ? string.Empty : "…");
        }

        return lines.ToArray();
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
