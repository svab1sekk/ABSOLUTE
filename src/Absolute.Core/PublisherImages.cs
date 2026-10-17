using System.Text.Json;
using System.Text.RegularExpressions;

namespace Absolute.Core;

public static class PublisherImages
{
    public static string? Fortnite(JsonElement root)
    {
        // Epic's live lobby background changes with the season and avoids shop promotions.
        if (root.TryGetProperty("dynamicbackgrounds", out var backgrounds))
        {
            var choices = Objects(backgrounds).Where(e => e.TryGetProperty("backgroundimage", out _)).ToArray();
            var selected = choices.FirstOrDefault(e => Text(e, "stage").Equals("default", StringComparison.OrdinalIgnoreCase));
            if (selected.ValueKind == JsonValueKind.Undefined) selected = choices.FirstOrDefault();
            if (selected.ValueKind == JsonValueKind.Object && Https(Text(selected, "backgroundimage")) is string current) return current;
        }
        var entries = Objects(root).Where(e => e.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String)
            .Select(e => new { Image = Https(Text(e, "image")), Date = Date(e), Title = Text(e, "title") + " " + Text(e, "body") })
            .Where(e => e.Image is not null).ToArray();
        var season = entries.Where(e => Regex.IsMatch(e.Title, @"\bseason\b|\bchapter\b|battle royale", RegexOptions.IgnoreCase)).ToArray();
        return (season.Length > 0 ? season : entries).OrderByDescending(e => e.Date).Select(e => e.Image).FirstOrDefault();
    }

    public static string? LatestRiotArticle(string html, string game)
    {
        var path = game == "valorant" ? @"/en-us/news/game-updates/valorant-patch-notes-[a-z0-9-]+/?" : @"/en-us/news/game-updates/patch-[a-z0-9-]+notes/?";
        return Regex.Matches(html.Replace("\\/", "/", StringComparison.Ordinal), path, RegexOptions.IgnoreCase).Select(m => m.Value).FirstOrDefault();
    }

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? value : null;
    private static DateTimeOffset Date(JsonElement e)
    {
        foreach (var key in new[] { "publishedAt", "date", "_activeDate", "lastModified", "_lastModified" })
            if (DateTimeOffset.TryParse(Text(e, key), out var date)) return date;
        return DateTimeOffset.MinValue;
    }
    private static IEnumerable<JsonElement> Objects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            yield return e;
            foreach (var property in e.EnumerateObject()) foreach (var child in Objects(property.Value)) yield return child;
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) foreach (var child in Objects(item)) yield return child;
    }
}
