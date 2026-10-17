namespace Absolute.Core;

public static class GameArtwork
{
    public static string? Source(GameDefinition game)=>game.ArtworkUrl??(game.SteamAppId is int id?$"https://cdn.akamai.steamstatic.com/steam/apps/{id}/library_hero.jpg":game.Id switch
    {
        "fortnite"=>"https://fortnitecontent-website-prod07.ol.epicgames.com/content/api/pages/fortnite-game",
        "valorant"=>"https://playvalorant.com/en-us/news/game-updates/",
        "lol"=>"https://www.leagueoflegends.com/en-us/news/game-updates/",
        "osu"=>"https://raw.githubusercontent.com/ppy/osu-resources/a3423ae5fa76223b648db1df31875a952b588d69/osu.Game.Resources/Textures/Backgrounds/bg1.jpg",
        _=>null
    });
}
