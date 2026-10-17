using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using Absolute.Core;

namespace Absolute.App.Services;

internal static class MonitorSupportResolver
{
    private static readonly ConcurrentDictionary<string,(string Url,DateTime Checked)> Cache=new();

    public static async Task<string> ResolveAsync(string name,Func<string,CancellationToken,Task<string>> fetch,CancellationToken cancellationToken)
    {
        if(!name.StartsWith("TITAN ARMY ",StringComparison.OrdinalIgnoreCase))return "";
        var model=name[11..].Trim();
        if(Cache.TryGetValue(model,out var cached)&&DateTime.UtcNow-cached.Checked<TimeSpan.FromHours(cached.Url.Length>0?24:1))return cached.Url;
        var publisher=new Uri("https://www.titan-army.com/");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);budget.CancelAfter(TimeSpan.FromSeconds(18));
        var candidates=new List<string>();
        try
        {
            var catalog=await TryFetch("/products.json?limit=250");
            if(catalog.Length>0)
                try{candidates.AddRange(MonitorProductLinks.FromCatalog(catalog,publisher,model));}catch(JsonException){}
            if(candidates.Count==0)
            {
                var sitemap=await TryFetch("/sitemap.xml");
                if(sitemap.Length>0)
                {
                    var links=MonitorProductLinks.SitemapLinks(sitemap,publisher);
                    candidates.AddRange(links.Where(link=>MonitorProductLinks.MatchesModel(new Uri(link).AbsolutePath,model)));
                    foreach(var child in links.Where(link=>new Uri(link).AbsolutePath.Contains("sitemap",StringComparison.OrdinalIgnoreCase)&&link.Contains("product",StringComparison.OrdinalIgnoreCase)).Take(4))
                    {
                        var xml=await TryFetch(child);if(xml.Length==0)continue;
                        candidates.AddRange(MonitorProductLinks.SitemapLinks(xml,publisher).Where(link=>MonitorProductLinks.MatchesModel(new Uri(link).AbsolutePath,model)));
                    }
                }
            }
            if(candidates.Count==0)
            {
                var page=await TryFetch("/search?q="+Uri.EscapeDataString(model)+"&type=product");
                candidates.AddRange(MonitorProductLinks.FromPage(page,publisher,model));
            }
            foreach(var link in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
            {
                var html=await TryFetch(link);
                if(MonitorProductLinks.MatchesModel(html,model)){Cache[model]=(link,DateTime.UtcNow);return link;}
            }
        }
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or System.Xml.XmlException or ArgumentException){}
        cancellationToken.ThrowIfCancellationRequested();Cache[model]=("",DateTime.UtcNow);return "";

        async Task<string> TryFetch(string path)
        {
            try{return await fetch(new Uri(publisher,path).ToString(),budget.Token);}
            catch(HttpRequestException){return "";}
        }
    }
}
