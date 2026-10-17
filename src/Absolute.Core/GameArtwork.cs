namespace Absolute.Core;

public static class GameArtwork
{
    private static readonly IReadOnlyDictionary<string,int> SteamIds=new Dictionary<string,int>
    {
        ["cs2"]=730,["overwatch"]=2357570,["rocketleague"]=252950,["apex"]=1172470,["siege"]=359550,
        ["dota2"]=570,["pubg"]=578080,["deadlock"]=1422450,["rivals"]=2767030,["warzone"]=1938090,
        ["finals"]=2073850,["deltaforce"]=2507950,["tekken"]=1778820,["sf6"]=1364780,
        ["brawlhalla"]=291550,["smite2"]=2437170,["paladins"]=444090,["tf2"]=440,["quake"]=611500,
        ["halo"]=1240440,["hunt"]=594650,["rust"]=252490,["trackmania"]=2225070,["iracing"]=266410,
        ["acc"]=805550,["fc"]=2669320,["efootball"]=1665460
    };
    public static string? Source(GameDefinition game)=>game.ArtworkUrl??(SteamIds.TryGetValue(game.Id,out var id)?$"https://cdn.akamai.steamstatic.com/steam/apps/{id}/library_hero.jpg":game.Id switch
    {
        "fortnite"=>"https://store.epicgames.com/en-US/p/fortnite",
        "valorant"=>"https://playvalorant.com/en-us/",
        "lol"=>"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/Ahri_0.jpg",
        "tarkov"=>"https://www.escapefromtarkov.com/",
        "osu"=>"https://osu.ppy.sh/",
        _=>null
    });
}
