using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Absolute.Core;

public static class MonitorProductLinks
{
    public static bool MatchesModel(string text,string model)
    {
        var characters=Regex.Replace(model,"[^A-Za-z0-9]","");
        if(characters.Length<4)return false;
        var pattern="(?<![A-Za-z0-9])"+string.Join(@"[\s_-]*",characters.Select(character=>Regex.Escape(character.ToString())))+"(?![A-Za-z0-9])";
        return Regex.IsMatch(WebUtility.HtmlDecode(Uri.UnescapeDataString(text)),pattern,RegexOptions.IgnoreCase);
    }

    public static IReadOnlyList<string> FromCatalog(string json,Uri publisher,string model)
    {
        using var document=JsonDocument.Parse(json);var links=new List<string>();
        if(!document.RootElement.TryGetProperty("products",out var products)||products.ValueKind!=JsonValueKind.Array)return links;
        foreach(var product in products.EnumerateArray())
        {
            var title=VendorDrivers.Value(product,"title");var handle=VendorDrivers.Value(product,"handle");
            if(MatchesModel(title,model)&&Regex.IsMatch(handle,@"^[a-zA-Z0-9_-]+$"))links.Add(new Uri(publisher,"/products/"+handle).ToString());
        }
        return links;
    }

    public static IReadOnlyList<string> SitemapLinks(string xml,Uri publisher)
    {
        return XDocument.Parse(xml).Descendants().Where(element=>element.Name.LocalName=="loc")
            .Select(element=>element.Value.Trim()).Where(link=>IsPublisherPage(link,publisher)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> FromPage(string html,Uri publisher,string model)
    {
        var result=new List<string>();
        foreach(Match match in Regex.Matches(html,"<a\\b[^>]*href\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>(?<text>[\\s\\S]*?)</a>",RegexOptions.IgnoreCase))
        {
            if(!Uri.TryCreate(publisher,WebUtility.HtmlDecode(match.Groups["url"].Value),out var uri)||!IsPublisherPage(uri.ToString(),publisher))continue;
            if(uri.AbsolutePath.Contains("/search",StringComparison.OrdinalIgnoreCase))continue;
            if(MatchesModel(uri.AbsolutePath,model)||MatchesModel(Regex.Replace(match.Groups["text"].Value,"<[^>]+>"," "),model))result.Add(uri.ToString());
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsPublisherPage(string link,Uri publisher)=>Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&uri.IsDefaultPort&&Host(uri)==Host(publisher);
    private static string Host(Uri uri)=>uri.Host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?uri.Host[4..].ToLowerInvariant():uri.Host.ToLowerInvariant();
}
