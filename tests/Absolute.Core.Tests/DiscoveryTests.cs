using Absolute.Core;
using Xunit;

namespace Absolute.Core.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void SteamLibrariesDecodeVdfPathsAndIgnoreUnrelatedKeys()
    {
        const string vdf="""
        "libraryfolders" { "0" { "path" "C:\\Program Files (x86)\\Steam" "label" "Games" }
        "1" { "path" "D:\\SteamLibrary" } }
        """;
        Assert.Equal([@"C:\Program Files (x86)\Steam",@"D:\SteamLibrary"],SteamManifest.LibraryPaths(vdf));
        Assert.Equal("Counter-Strike Source",SteamManifest.InstallDirectory("\"installdir\" \"Counter-Strike Source\""));
        Assert.Null(SteamManifest.InstallDirectory("\"installdir\" \"../other\""));
        Assert.Null(SteamManifest.InstallDirectory("\"installdir\" \"C:\\\\other\""));
    }

    [Fact]
    public void SharedEngineNamesAreMatchedOnlyInsideTheirActualInstallation()
    {
        Assert.True(SteamManifest.ContainsExecutable(@"D:\SteamLibrary\common\Counter-Strike Source",@"d:\steamlibrary\common\Counter-Strike Source\hl2.exe"));
        Assert.False(SteamManifest.ContainsExecutable(@"D:\Games\Game",@"D:\Games\Game 2\game.exe"));
        Assert.False(SteamManifest.ContainsExecutable(@"D:\Games\Game",@"D:\Games\Game\..\Other\game.exe"));
    }

    [Theory]
    [InlineData("TITAN ARMY P25S Plus 240 Hz",true)]
    [InlineData("/products/24-inch-p25s-plus-monitor",true)]
    [InlineData("P25S",false)]
    [InlineData("P2510S Plus",false)]
    [InlineData("P25S Plus2",false)]
    public void MonitorLookupRequiresTheExactModel(string title,bool expected)=>Assert.Equal(expected,MonitorProductLinks.MatchesModel(title,"P25S Plus"));

    [Fact]
    public void MonitorCatalogProducesActualProductLinksAndRejectsOtherModels()
    {
        const string catalog="""
        {"products":[{"title":"TITAN ARMY P2510S Plus","handle":"wrong-monitor"},
        {"title":"TITAN ARMY P25S Plus","handle":"actual-catalog-handle"}]}
        """;
        Assert.Equal("https://www.titan-army.com/products/actual-catalog-handle",Assert.Single(MonitorProductLinks.FromCatalog(catalog,new Uri("https://www.titan-army.com/"),"P25S Plus")));
    }

    [Fact]
    public void MonitorSitemapsAndPagesCannotReturnSearchOrThirdPartyLinks()
    {
        var publisher=new Uri("https://www.titan-army.com/");
        const string sitemap="""
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://titan-army.com/products/p25s-plus</loc></url>
        <url><loc>https://www.titan-army.com.evil.example/products/p25s-plus</loc></url></urlset>
        """;
        Assert.Equal("https://titan-army.com/products/p25s-plus",Assert.Single(MonitorProductLinks.SitemapLinks(sitemap,publisher)));
        const string page="""
        <a href="/search?q=P25S+Plus">P25S Plus</a><a href="https://other.example/p25s-plus">P25S Plus</a>
        <a href="/products/p25s-plus">P25S Plus</a>
        """;
        Assert.Equal("https://www.titan-army.com/products/p25s-plus",Assert.Single(MonitorProductLinks.FromPage(page,publisher,"P25S Plus")));
    }
}
