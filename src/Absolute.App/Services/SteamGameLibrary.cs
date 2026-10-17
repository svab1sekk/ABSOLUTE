using System.IO;
using Microsoft.Win32;
using Absolute.Core;

namespace Absolute.App.Services;

internal static class SteamGameLibrary
{
    private static DateTime refreshed;
    private static IReadOnlyList<(GameDefinition Game,string Directory)> installations=[];

    public static IReadOnlyList<(GameDefinition Game,string Directory)> Installed(IEnumerable<GameDefinition> games)
    {
        if(DateTime.UtcNow-refreshed<TimeSpan.FromSeconds(45))return installations;
        var result=new List<(GameDefinition,string)>();
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var root=key?.GetValue("SteamPath") as string;
            if(string.IsNullOrWhiteSpace(root))return [];
            var libraries=new[]{root}.Concat(SteamManifest.LibraryPaths(ReadSmallFile(Path.Combine(root,"steamapps","libraryfolders.vdf")))).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach(var library in libraries)
                foreach(var game in games.Where(game=>game.SteamAppId is not null))
                {
                    var manifest=ReadSmallFile(Path.Combine(library,"steamapps",$"appmanifest_{game.SteamAppId}.acf"));
                    if(SteamManifest.InstallDirectory(manifest) is string directory)
                        result.Add((game,Path.GetFullPath(Path.Combine(library,"steamapps","common",directory))));
                }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException){}
        installations=result;refreshed=DateTime.UtcNow;return installations;
    }

    private static string ReadSmallFile(string path)
    {
        try{return File.Exists(path)&&new FileInfo(path).Length<=512*1024?File.ReadAllText(path):"";}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){return "";}
    }
}
