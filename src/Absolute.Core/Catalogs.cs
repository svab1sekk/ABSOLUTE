using System.Text.RegularExpressions;

namespace Absolute.Core;

public sealed record GameDefinition(string Id, string Name, string[] Processes, string Accent, int Banner, string? Executable = null, string? ArtworkUrl = null);
public sealed record PlaybookDefinition(string Id, string Name, string Detail, string Accent, int Banner, string? Website = null, string Kind = "OS playbook", string Maker = "", string? AvatarUrl = null, string[]? Facts = null);
public sealed record ComponentBenchmarkResult(string Component, double Score, string Unit, double Seconds, DateTimeOffset RecordedAt, string Detail);
public sealed record GameSessionJournal(string GameId, Guid? PreviousPower, Guid? AppliedPower, int ProcessId, long ProcessStartTicks, string? PreviousPriority, string? AppliedPriority);
public sealed record HardwarePart(string Category, string Name, IReadOnlyList<KeyValuePair<string,string>> Details, string Id = "", string[]? HardwareIds = null);
public sealed record SensorReading(string Name, string Hardware, string Type, float Value);
public sealed record DriverDevice(string Id, string Name, string Category, string Manufacturer, string Provider, string Version, string Date, string[] HardwareIds, bool? Signed, int? ProblemCode = null);

public static class HardwareNames
{
    public static string Clean(string value) => Regex.Replace(Regex.Replace(value, @"\((?:R|TM|C)\)|[®™]", "", RegexOptions.IgnoreCase), @"\s+", " ").Trim();
}

public static class Catalogs
{
    public static IReadOnlyList<GameDefinition> Games { get; } =
    [
        new("fortnite", "Fortnite", ["FortniteClient-Win64-Shipping"], "#9B72D7", 0),
        new("cs2", "Counter-Strike 2", ["cs2"], "#C28D48", 1),
        new("valorant", "VALORANT", ["VALORANT-Win64-Shipping"], "#D16679", 2),
        new("lol", "League of Legends", ["League of Legends"], "#60AB9C", 3),
        new("overwatch", "Overwatch 2", ["Overwatch"], "#D49A62", 4),
        new("rocketleague", "Rocket League", ["RocketLeague"], "#6C9FDB", 5),
        new("apex", "Apex Legends", ["r5apex", "r5apex_dx12"], "#C56B64", 6),
        new("siege", "Rainbow Six Siege", ["RainbowSix", "RainbowSix_Vulkan", "RainbowSix_DX12"], "#729EC4", 7),
        new("dota2", "Dota 2", ["dota2"], "#C37868", 8),
        new("pubg", "PUBG: Battlegrounds", ["TslGame"], "#B7A46F", 9),
        new("deadlock", "Deadlock", ["deadlock"], "#74AD92", 10),
        new("rivals", "Marvel Rivals", ["Marvel-Win64-Shipping"], "#BB779C", 11),
        new("warzone", "Call of Duty / Warzone", ["cod", "cod22", "cod23"], "#8FA96D", 12),
        new("finals", "THE FINALS", ["Discovery"], "#B58BDC", 13),
        new("deltaforce", "Delta Force", ["DeltaForceClient-Win64-Shipping"], "#72A7AB", 14),
        new("tekken", "TEKKEN 8", ["Polaris-Win64-Shipping"], "#BD7BAA", 15),
        new("sf6", "Street Fighter 6", ["StreetFighter6"], "#D69A6B", 16),
        new("brawlhalla", "Brawlhalla", ["Brawlhalla"], "#B5A3DC", 17),
        new("smite2", "SMITE 2", ["Smite2-Win64-Shipping"], "#B39D67", 18),
        new("paladins", "Paladins", ["Paladins"], "#79B5BF", 19),
        new("tf2", "Team Fortress 2", ["tf_win64"], "#BE9878", 20),
        new("quake", "Quake Champions", ["QuakeChampions"], "#B87A73", 21),
        new("halo", "Halo Infinite", ["HaloInfinite"], "#8CB0A3", 22),
        new("tarkov", "Escape from Tarkov", ["EscapeFromTarkov"], "#9CA880", 23),
        new("hunt", "Hunt: Showdown", ["HuntGame"], "#A57D70", 24),
        new("rust", "Rust", ["RustClient"], "#B38D74", 25),
        new("trackmania", "Trackmania", ["Trackmania"], "#82B7A0", 26),
        new("iracing", "iRacing", ["iRacingSim64DX11"], "#779DCF", 27),
        new("acc", "Assetto Corsa Competizione", ["AC2-Win64-Shipping"], "#BD828A", 28),
        new("fc", "EA SPORTS FC", ["FC25", "FC26"], "#90B791", 29),
        new("efootball", "eFootball", ["eFootball"], "#8F99D3", 30),
        new("osu", "osu!", ["osu"], "#C485AC", 31)
    ];

    public static IReadOnlyList<PlaybookDefinition> Playbooks { get; } =
    [
        new("atlas", "AtlasOS", "", "#9476CB", 0, "https://github.com/Atlas-OS/Atlas", "AME playbook", "AtlasOS team", "https://github.com/Atlas-OS.png?size=128", ["Configurable app removal", "Gaming and service defaults", "AME Wizard"]),
        new("revios", "ReviOS", "", "#78AC9A", 0, "https://github.com/meetrevision/playbook", "AME playbook", "Revision", "https://github.com/meetrevision.png?size=128", ["Windows privacy settings", "Bundled app cleanup", "Revision management tools"]),
        new("ame11", "AME 11", "", "#A199AC", 0, "https://ameliorated.io/", "AME playbook", "Ameliorated", "https://github.com/Ameliorated-LLC.png?size=128", ["Windows 11 customization", "Removes bundled components", "Reduces online integrations"]),
        new("fsosx", "FSOSX", "", "#B17D87", 0, "https://fsosx.com/", "Windows project", "FSOSX", "https://fsosx.com/", ["Custom Windows setup", "Gaming configuration", "Project installation guide"]),
        new("atmosphere", "AtmosphereOS", "", "#8EA6CE", 0, "https://github.com/AtmosphereTeam/AtmosphereOS", "AME playbook", "Atmosphere Team", "https://github.com/AtmosphereTeam.png?size=128", ["Modular Windows settings", "Privacy and interface choices", "Based on AtlasOS"]),
        new("rapid", "RapidOS", "", "#9E88CA", 0, "https://github.com/rapid-community/RapidOS", "AME playbook", "Rapid Community", "https://github.com/rapid-community.png?size=128", ["Telemetry configuration", "App and service cleanup", "AME Wizard"]),
        new("atom", "AtomOS", "", "#89A5B4", 0, "https://github.com/ProjectAtomOS/Atom-Playbook", "AME playbook", "Project AtomOS", "https://github.com/ProjectAtomOS.png?size=128", ["Windows customization", "Privacy and app choices", "Optional Atom theme"]),
        new("arkanoid", "ArkanoidOS Lite", "", "#BE98AF", 0, "https://github.com/jointhearkanoid/playbook", "AME playbook", "ArkanoidOS community", "https://github.com/jointhearkanoid.png?size=128", ["Based on ReviOS", "App and component cleanup", "Modular Windows interface"]),
        new("ultra", "UltraOS", "", "#A299C1", 0, "https://github.com/UltraOS-Project/playbook", "AME playbook", "UltraOS Project", "https://github.com/UltraOS-Project.png?size=128", ["Tiered Windows configuration", "Optional advanced changes", "Project restore actions"]),
        new("tiny11", "tiny11builder", "", "#809DC2", 0, "https://github.com/ntdevlabs/tiny11builder", "Image builder", "NTDEV", "https://github.com/ntdevlabs.png?size=128", ["Uses your Windows ISO", "Removes selected components", "Creates an installation image"]),
        new("microwin", "MicroWin", "", "#ACA3B8", 0, "https://github.com/ChrisTitusTech/winutil", "Image builder", "Chris Titus Tech & contributors", "https://github.com/ChrisTitusTech.png?size=128", ["Part of WinUtil", "Customizes a Windows ISO", "Installation-time app choices"])
    ];
}
