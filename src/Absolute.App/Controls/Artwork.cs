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
    private static readonly string Cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Absolute","Artwork","publisher-v2");
    public static IReadOnlyDictionary<string,string> Status=>Results;

    static Artwork()=>Client.DefaultRequestHeaders.UserAgent.ParseAdd("ABSOLUTE/0.5 (+https://github.com/svab1sekk/ABSOLUTE)");

    public static Grid Scene(GameDefinition game,double height,bool hero=false)
    {
        var scene=new Grid{Height=height,Background=Brush("#19121F"),ClipToBounds=true};
        scene.SizeChanged+=(_,_)=>scene.Clip=new RectangleGeometry(new Rect(0,0,scene.ActualWidth,scene.ActualHeight),12,12);
        if(hero)scene.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(244,11,8,16),Color.FromArgb(48,11,8,16),0)});
        scene.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(hero?(byte)20:(byte)0,11,8,16),Color.FromArgb(238,11,8,16),90)});
        _=ApplyAsync();return scene;
        async Task ApplyAsync()
        {
            var bitmap=await GetAsync(game,hero?1400:480);
            if(bitmap is not null)scene.Background=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,AlignmentX=hero?AlignmentX.Right:AlignmentX.Center};
        }
    }

    public static Task<BitmapSource?> GetAsync(GameDefinition game,int width=480)=>Images.GetOrAdd(game.Id+":"+width,_=>Task.Run(()=>LoadAsync(game,width)));
    public static Task WarmAsync(IEnumerable<GameDefinition> games)=>Task.WhenAll(games.Select(game=>GetAsync(game)));

    private static async Task<BitmapSource?> LoadAsync(GameDefinition game,int width)
    {
        var source=GameArtwork.Source(game);if(source is null){Results[game.Id]="No publisher artwork";return null;}
        var resolved=source;
        Results[game.Id]="Loading publisher artwork";
        if(!Regex.IsMatch(game.Id,@"^[a-zA-Z0-9-]{1,80}$")){Results[game.Id]="Invalid cache key";return null;}
        var path=Path.Combine(Cache,game.Id+".img");
        try
        {
            if(File.Exists(path)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromHours(24))
            {
                try{var cached=Decode(await File.ReadAllBytesAsync(path),width);Results[game.Id]="Cached publisher artwork";return cached;}
                catch(Exception ex) when(ex is IOException or NotSupportedException or FileFormatException){File.Delete(path);}
            }
            await Downloads.WaitAsync();
            try
            {
                var uri=new Uri(source);(byte[] Bytes,bool Html) response;
                try{response=await FetchAsync(uri);}
                catch(HttpRequestException)when(game.SteamAppId is int)
                {uri=new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{game.SteamAppId}/header.jpg");resolved=uri.ToString();response=await FetchAsync(uri);}
                if(game.Id=="fortnite")
                {
                    using var content=JsonDocument.Parse(response.Bytes);var root=content.RootElement;
                    var imageUrl=PublisherImages.Fortnite(root);
                    uri=new Uri(imageUrl??throw new InvalidDataException("Fortnite's public news feed did not expose artwork."));
                    resolved=uri.ToString();response=await FetchAsync(uri);
                }
                if(response.Html&&game.Id is "valorant" or "lol")
                {
                    var html=System.Text.Encoding.UTF8.GetString(response.Bytes);
                    if(PublisherImages.LatestRiotArticle(html,game.Id) is string article)
                    {uri=new Uri(uri,article);resolved=uri.ToString();response=await FetchAsync(uri);}
                    else throw new InvalidDataException("The publisher did not expose a current game-update article.");
                }
                if(response.Html)
                {
                    var html=System.Text.Encoding.UTF8.GetString(response.Bytes);
                    var imageUrl=(game.Id.StartsWith("project-",StringComparison.Ordinal)?FindSiteIcon(html):FindOpenGraphImage(html))??throw new InvalidDataException("The publisher did not expose a cover image.");
                    uri=new Uri(uri,WebUtility.HtmlDecode(imageUrl));
                    if(uri.Host.EndsWith("rgpub.io",StringComparison.OrdinalIgnoreCase))
                    {
                        var builder=new UriBuilder(uri);var parameters=builder.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(p=>!p.StartsWith("auto=",StringComparison.OrdinalIgnoreCase)&&!p.StartsWith("fm=",StringComparison.OrdinalIgnoreCase));
                        builder.Query=string.Join("&",parameters.Append("fm=jpg"));uri=builder.Uri;
                    }
                    resolved=uri.ToString();response=await FetchAsync(uri);
                }
                if(response.Html)throw new InvalidDataException("The publisher returned a page instead of an image.");
                var bitmap=Decode(response.Bytes,width);Directory.CreateDirectory(Cache);
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temporary,response.Bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                Results[game.Id]="Publisher artwork · "+resolved;return bitmap;
            }
            finally{Downloads.Release();}
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or NotSupportedException or UriFormatException or FileFormatException or JsonException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if(File.Exists(path))
                try{var cached=Decode(await File.ReadAllBytesAsync(path),width);Results[game.Id]="Cached artwork · refresh unavailable";return cached;}catch(Exception cacheError)when(cacheError is IOException or FileFormatException or NotSupportedException){}
            Results[game.Id]=ex.Message+" · "+resolved;return null;
        }
    }

    private static BitmapSource Decode(byte[] bytes,int width)
    {
        using var stream=new MemoryStream(bytes,false);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
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

    public static FrameworkElement ProjectAvatar(PlaybookDefinition project)
    {
        var image=new Image{Width=48,Height=48,Stretch=Stretch.Uniform,RenderTransformOrigin=new Point(.5,.5)};
        var frame=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(12),Background=Brush("#211A29"),Child=image,ClipToBounds=true};
        frame.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(0,0,48,48),12,12);
        if(project.AvatarUrl is string source)_=Load();
        return frame;
        async Task Load()
        {
            var definition=new GameDefinition("project-"+project.Id,project.Name,[],project.Accent,0,ArtworkUrl:project.AvatarUrl);
            image.Source=await GetAsync(definition,128);
        }
    }
    public static Task WarmProjectsAsync()=>Task.WhenAll(Catalogs.Playbooks.Where(p=>p.AvatarUrl is not null).Select(p=>GetAsync(new GameDefinition("project-"+p.Id,p.Name,[],p.Accent,0,ArtworkUrl:p.AvatarUrl),128)));

    private static string? FindSiteIcon(string html)
    {
        foreach(Match tag in Regex.Matches(html,@"<link\b[^>]*>",RegexOptions.IgnoreCase))
        {
            if(!Regex.IsMatch(tag.Value,"rel\\s*=\\s*[\"'](?:apple-touch-icon|icon|shortcut icon)[\"']",RegexOptions.IgnoreCase))continue;
            var url=Regex.Match(tag.Value,"href\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",RegexOptions.IgnoreCase).Groups["url"].Value;
            if(url.Length>0&&!url.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))return url;
        }
        return FindOpenGraphImage(html);
    }

    private static bool AllowedHost(string host)=>new[]{"steamstatic.com","steamusercontent.com","epicgames.com","unrealengine.com","fortnite.com","playvalorant.com","rgpub.io","riotgames.com","leagueoflegends.com","escapefromtarkov.com","osu.ppy.sh","assets.ppy.sh","contentstack.io","raw.githubusercontent.com","github.com","githubusercontent.com","fsosx.com","ameliorated.io","amelabs.net","blizzard.com","battle.net","playruneterra.com","tarkov.com","bnetcmsus-a.akamaihd.net","blz-contentstack-images.akamaized.net","blz-contentstack.com"}.Any(allowed=>host.Equals(allowed,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+allowed,StringComparison.OrdinalIgnoreCase));

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
