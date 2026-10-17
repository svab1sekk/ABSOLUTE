using System.Text.RegularExpressions;

namespace Absolute.Core;

public sealed record GameDefinition(string Id, string Name, string[] Processes, string Accent, int Banner, string? Executable = null);
public sealed record PlaybookDefinition(string Id, string Name, string Detail, string Accent, int Banner, string? Website = null, string Kind = "Settings profile");
public sealed record ComponentBenchmarkResult(string Component, double Score, string Unit, double Seconds, DateTimeOffset RecordedAt, string Detail);
public sealed record GameSessionJournal(string GameId, Guid? PreviousPower, Guid? AppliedPower, int ProcessId, long ProcessStartTicks, string? PreviousPriority, string? AppliedPriority);
public sealed record HardwarePart(string Category, string Name, IReadOnlyList<KeyValuePair<string,string>> Details);
public sealed record SensorReading(string Name, string Hardware, string Type, float Value);
public sealed record DriverDevice(string Id, string Name, string Category, string Manufacturer, string Provider, string Version, string Date, string[] HardwareIds, bool? Signed, int? ProblemCode = null);
public sealed record DriverUpdate(string Title, string HardwareId, string Provider, string Version);
public sealed record DriverSearchResult(IReadOnlyList<DriverUpdate> Updates, DateTimeOffset CheckedAt, string? Error = null);

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
        new("competitive", "Competitive", "Game Mode on. Background game recording off.", "#A080CD", 0),
        new("balanced", "Balanced", "Game Mode on. Keep Windows capture preferences.", "#7E9FCC", 1),
        new("focus", "Focus", "Game Mode on. Toast notifications and background capture off.", "#74AAA4", 2),
        new("streaming", "Streaming", "Game Mode on. Leave capture available for recording.", "#B49A72", 3),
        new("atlas", "AtlasOS", "Windows OS playbook", "#9476CB", 4, "https://github.com/Atlas-OS/Atlas", "External playbook"),
        new("revios", "ReviOS", "Windows OS playbook", "#78AC9A", 5, "https://github.com/meetrevision/playbook", "External playbook"),
        new("ame11", "AME 11", "Ameliorated Windows playbook", "#A199AC", 6, "https://ameliorated.io/", "External playbook"),
        new("fsosx", "FSOSX", "Custom Windows project", "#B17D87", 7, "https://fsosx.com/", "External project"),
        new("tiny11", "tiny11builder", "Windows image builder", "#809DC2", 8, "https://github.com/ntdevlabs/tiny11builder", "OS image builder"),
        new("microwin", "MicroWin", "Windows image customization", "#ACA3B8", 9, "https://github.com/ChrisTitusTech/winutil", "OS image builder"),
        new("winutil", "WinUtil", "Windows tuning toolkit", "#91AF90", 10, "https://github.com/ChrisTitusTech/winutil", "Tuning toolkit"),
        new("sophia", "Sophia Script", "Windows configuration toolkit", "#929BC8", 11, "https://github.com/farag2/Sophia-Script-for-Windows", "Tuning toolkit"),
        new("privacy", "privacy.sexy", "Auditable Windows privacy scripts", "#A081BA", 6, "https://github.com/undergroundwires/privacy.sexy", "Tuning toolkit"),
        new("optimizer", "Optimizer", "Windows settings utility", "#799DAF", 8, "https://github.com/hellzerg/optimizer", "Tuning toolkit"),
        new("winslap", "WinSlap", "Windows configuration utility", "#B49A88", 9, "https://github.com/svenmauch/WinSlap", "Tuning toolkit"),
        new("debloat", "Win11Debloat", "Windows configuration scripts", "#91A897", 10, "https://github.com/Raphire/Win11Debloat", "Tuning toolkit"),
        new("unattended", "UnattendedWinstall", "Windows installation customization", "#9D92BE", 11, "https://github.com/memstechtips/UnattendedWinstall", "Install configuration"),
        new("rectify", "Rectify11", "Windows desktop customization", "#86A6BE", 5, "https://github.com/Rectify11/Installer", "Customization")
    ];
}
