using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App.Controls;

internal static class Artwork
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(18)};
    private static readonly SemaphoreSlim Downloads=new(6,6);
    private static readonly ConcurrentDictionary<string,Task<BitmapSource?>> Images=new();
    private static readonly ConcurrentDictionary<string,string> Results=new();
    private static readonly string Cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Absolute","Artwork","publisher-v1");
    public static IReadOnlyDictionary<string,string> Status=>Results;

    static Artwork()=>Client.DefaultRequestHeaders.UserAgent.ParseAdd("ABSOLUTE/0.3 (+https://github.com/svab1sekk/ABSOLUTE)");

    public static Grid Scene(GameDefinition game,double height,bool hero=false)
    {
        var scene=new Grid{Height=height,Background=Brush("#19121F"),ClipToBounds=true};
        scene.SizeChanged+=(_,_)=>scene.Clip=new RectangleGeometry(new Rect(0,0,scene.ActualWidth,scene.ActualHeight),12,12);
        if(hero)scene.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(244,11,8,16),Color.FromArgb(48,11,8,16),0)});
        scene.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(hero?(byte)20:(byte)0,11,8,16),Color.FromArgb(238,11,8,16),90)});
        _=ApplyAsync();return scene;
        async Task ApplyAsync()
        {
            var bitmap=await GetAsync(game);
            if(bitmap is not null)scene.Background=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,AlignmentX=hero?AlignmentX.Right:AlignmentX.Center};
        }
    }

    public static Task<BitmapSource?> GetAsync(GameDefinition game)=>Images.GetOrAdd(game.Id,_=>Task.Run(()=>LoadAsync(game)));
    public static Task WarmAsync(IEnumerable<GameDefinition> games)=>Task.WhenAll(games.Select(GetAsync));

    private static async Task<BitmapSource?> LoadAsync(GameDefinition game)
    {
        var source=GameArtwork.Source(game);if(source is null){Results[game.Id]="No publisher artwork";return null;}
        var resolved=source;
        Results[game.Id]="Loading publisher artwork";
        if(!Regex.IsMatch(game.Id,@"^[a-zA-Z0-9-]{1,80}$")){Results[game.Id]="Invalid cache key";return null;}
        var path=Path.Combine(Cache,game.Id+".img");
        try
        {
            if(File.Exists(path))
            {
                try{var cached=Decode(await File.ReadAllBytesAsync(path));Results[game.Id]="Cached publisher artwork";return cached;}
                catch(Exception ex) when(ex is IOException or NotSupportedException or FileFormatException){File.Delete(path);}
            }
            await Downloads.WaitAsync();
            try
            {
                var uri=new Uri(source);var response=await FetchAsync(uri);
                if(game.Id=="fortnite")
                {
                    using var content=JsonDocument.Parse(response.Bytes);var root=content.RootElement;
                    var imageUrl=root.TryGetProperty("battleroyalenewsv2",out var modern)?FindNewsImage(modern):null;
                    imageUrl??=root.TryGetProperty("battleroyalenews",out var classic)?FindNewsImage(classic):null;
                    imageUrl??=FindNewsImage(root);
                    uri=new Uri(imageUrl??throw new InvalidDataException("Fortnite's public news feed did not expose artwork."));
                    resolved=uri.ToString();response=await FetchAsync(uri);
                }
                if(response.Html)
                {
                    var html=System.Text.Encoding.UTF8.GetString(response.Bytes);
                    var imageUrl=FindOpenGraphImage(html)??throw new InvalidDataException("The publisher did not expose a cover image.");
                    uri=new Uri(uri,WebUtility.HtmlDecode(imageUrl));
                    if(uri.Host.EndsWith("rgpub.io",StringComparison.OrdinalIgnoreCase))
                    {
                        var builder=new UriBuilder(uri);var parameters=builder.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(p=>!p.StartsWith("auto=",StringComparison.OrdinalIgnoreCase)&&!p.StartsWith("fm=",StringComparison.OrdinalIgnoreCase));
                        builder.Query=string.Join("&",parameters.Append("fm=jpg"));uri=builder.Uri;
                    }
                    resolved=uri.ToString();response=await FetchAsync(uri);
                }
                if(response.Html)throw new InvalidDataException("The publisher returned a page instead of an image.");
                var bitmap=Decode(response.Bytes);Directory.CreateDirectory(Cache);
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temporary,response.Bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                Results[game.Id]="Publisher artwork";return bitmap;
            }
            finally{Downloads.Release();}
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or NotSupportedException or UriFormatException or FileFormatException or JsonException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {Results[game.Id]=ex.Message+" · "+resolved;return null;}
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1600;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }

    private static async Task<(byte[] Bytes,bool Html)> FetchAsync(Uri uri)
    {
        if(uri.Scheme!="https"||!AllowedHost(uri.Host))throw new InvalidDataException("Artwork must come from an approved publisher host.");
        using var response=await GetResponseAsync(uri);response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri is not Uri final||final.Scheme!="https"||!AllowedHost(final.Host))throw new InvalidDataException("The artwork redirect was not a publisher host.");
        const int limit=12*1024*1024;
        if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Artwork exceeds the size limit.");
        using var stream=await response.Content.ReadAsStreamAsync();using var memory=new MemoryStream();var buffer=new byte[81920];
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(18));
        int read;while((read=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+read>limit)throw new InvalidDataException("Artwork exceeds the size limit.");memory.Write(buffer,0,read);}
        return(memory.ToArray(),response.Content.Headers.ContentType?.MediaType?.Contains("html",StringComparison.OrdinalIgnoreCase)==true);
    }

    private static async Task<HttpResponseMessage> GetResponseAsync(Uri uri)
    {
        for(var redirect=0;redirect<6;redirect++)
        {
            if(uri.Scheme!="https"||!AllowedHost(uri.Host))throw new InvalidDataException("The artwork redirect was not a publisher host.");
            var response=await Client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead);
            if((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))return response;
            var location=response.Headers.Location;response.Dispose();
            if(location is null)throw new InvalidDataException("The publisher returned an empty redirect.");uri=new Uri(uri,location);
        }
        throw new InvalidDataException("The publisher returned too many redirects.");
    }

    private static string? FindNewsImage(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in element.EnumerateObject())
                if(property.Name.Equals("image",StringComparison.OrdinalIgnoreCase)&&property.Value.ValueKind==JsonValueKind.String&&Uri.TryCreate(property.Value.GetString(),UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&AllowedHost(uri.Host))return uri.ToString();
            foreach(var property in element.EnumerateObject())if(FindNewsImage(property.Value) is {} image)return image;
        }
        else if(element.ValueKind==JsonValueKind.Array)
            foreach(var item in element.EnumerateArray())if(FindNewsImage(item) is {} image)return image;
        return null;
    }

    private static bool AllowedHost(string host)=>new[]{"steamstatic.com","steamusercontent.com","epicgames.com","unrealengine.com","fortnite.com","playvalorant.com","rgpub.io","riotgames.com","leagueoflegends.com","escapefromtarkov.com","osu.ppy.sh","assets.ppy.sh","contentstack.io","raw.githubusercontent.com"}.Any(allowed=>host.Equals(allowed,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+allowed,StringComparison.OrdinalIgnoreCase));

    private static string? FindOpenGraphImage(string html)
    {
        foreach(Match tag in Regex.Matches(html,@"<meta\b[^>]*>",RegexOptions.IgnoreCase))
        {
            if(!Regex.IsMatch(tag.Value,"(?:property|name)\\s*=\\s*[\"'](?:og:image|twitter:image)[\"']",RegexOptions.IgnoreCase))continue;
            var content=Regex.Match(tag.Value,"content\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",RegexOptions.IgnoreCase);
            if(content.Success)return content.Groups["url"].Value;
        }
        return null;
    }
    public static Thickness CardMargin(int index)=>(index%3) switch{0=>new Thickness(0,0,8,18),1=>new Thickness(4,0,4,18),_=>new Thickness(8,0,0,18)};
}
