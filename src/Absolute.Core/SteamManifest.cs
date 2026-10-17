using System.Text.RegularExpressions;

namespace Absolute.Core;

public static class SteamManifest
{
    public static IReadOnlyList<string> LibraryPaths(string text)=>Regex.Matches(text,"\"path\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",RegexOptions.IgnoreCase)
        .Select(match=>Unescape(match.Groups["value"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string? InstallDirectory(string text)
    {
        var match=Regex.Match(text,"\"installdir\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",RegexOptions.IgnoreCase);
        if(!match.Success)return null;
        var value=Unescape(match.Groups["value"].Value);
        return value.Length>0&&value is not "." and not ".."&&value.IndexOfAny(['\\','/',':'])<0?value:null;
    }

    public static bool ContainsExecutable(string installation,string executable)
    {
        var root=installation.Replace('\\','/').TrimEnd('/')+"/";var path=executable.Replace('\\','/');
        return !path.Split('/').Any(part=>part is "." or "..")&&path.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&path.Length>root.Length;
    }

    private static string Unescape(string value)=>Regex.Replace(value,@"\\([\\"" ])",match=>match.Groups[1].Value);
}
