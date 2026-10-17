using System.Text.Json;
using Absolute.Core;
using Xunit;

namespace Absolute.Core.Tests;

public sealed class Release04Tests
{
    [Theory]
    [InlineData("32.0.15.6094","560.94")]
    [InlineData("31.0.15.5176","551.76")]
    [InlineData("31.0.14.5239","452.39")]
    [InlineData("unknown","unknown")]
    public void NvidiaInfVersionsUseTheVendorNumber(string windows,string vendor)=>Assert.Equal(vendor,VendorDrivers.NvidiaVersion(windows));

    [Fact]
    public void DifferentVersionNamespacesCannotProduceAnUpdateBadge()
    {
        Assert.Null(VendorDrivers.CompareVersions("25.9.1","32.0.21029.1006"));
        Assert.Null(VendorDrivers.CompareVersions("beta 560.94","551.76"));
        Assert.True(VendorDrivers.CompareVersions("560.94","551.76")>0);
        Assert.True(VendorDrivers.CompareVersions("560.94","570.1")<0);
        Assert.Equal(0,VendorDrivers.CompareVersions("560.94","560.94"));
    }

    [Fact]
    public void NvidiaCatalogRejectsUntrustedDownloadHostsAndStudioOffers()
    {
        const string json="""
        {"IDS":[
          {"downloadInfo":{"Version":"999.99","DownloadURL":"https://nvidia.com.evil.example/driver.exe","IsCRD":"0","ReleaseDateTime":"2026-10-16"}},
          {"downloadInfo":{"Version":"580.1","DownloadURL":"https://international.download.nvidia.com/studio.exe","IsCRD":"1","ReleaseDateTime":"2026-10-15"}},
          {"downloadInfo":{"Version":"560.94","DownloadURL":"https://international.download.nvidia.com/driver.exe","IsCRD":"0","ReleaseDateTime":"2026-10-14"}}
        ]}
        """;
        var package=Assert.Single(VendorDrivers.ReadNvidia(json,"551.76"));
        Assert.Equal("560.94",package.Version);Assert.True(package.Comparison>0);
    }

    [Fact]
    public void MotherboardBundlesDoNotCompareAgainstAnUnrelatedInfVersion()
    {
        const string json="""
        {"Result":{"Obj":[
          {"Name":"Chipset","Files":[{"Title":"AMD Chipset Driver","Version":"V7.1.2.3","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/driver.zip"}}]},
          {"Name":"Audio","Files":[{"Title":"Audio","Version":"1.0","DownloadUrl":{"Global":"https://dlcdnets.asus.com/audio.zip"}}]}
        ]}}
        """;
        var driver=new DriverDevice("PCI\\VEN_1022","AMD GPIO Controller","SYSTEM","AMD","AMD","9.0.0.0","",[],true);
        var package=Assert.Single(VendorDrivers.ReadAsus(json,[driver]));
        Assert.Equal("7.1.2.3",package.Version);Assert.Null(package.Comparison);
        var matched=Assert.Single(VendorDrivers.ReadAsus(json,[driver],new Dictionary<string,string>{{"AMD","6.1.2.3"}}));
        Assert.Equal("6.1.2.3",matched.InstalledVersion);Assert.True(matched.Comparison>0);
    }

    [Fact]
    public void AmdCatalogComparesDriverStoreVersionsAndRequiresOneDownload()
    {
        const string page="<p>Windows Driver Store Version 32.0.21029.1006</p><a href=\"https://drivers.amd.com/drivers/whql-amd-software-win10-win11.exe\">Download</a>";
        var package=Assert.Single(VendorDrivers.ReadGpuPage(page,"AMD","32.0.21000.1000"));
        Assert.True(package.Comparison>0);
        Assert.Empty(VendorDrivers.ReadGpuPage(page+"<a href=\"https://drivers.amd.com/drivers/other-win10.exe\">Other GPU</a>","AMD",null));
    }

    [Fact]
    public void BenchmarkScoreKeepsEachHardwareCategoryEquallyWeighted()
    {
        ComponentBenchmarkResult Result(string name,double score)=>new(name,score,"",4,DateTimeOffset.UtcNow,"");
        var results=new[]{Result("CPU",1024),Result("RAM",8192),Result("GPU 3D",60),Result("GPU 2D",10000)};
        Assert.Equal(100,BenchmarkScoring.Combined(results)!.Value,6);
        var run=new BenchmarkRun("run",DateTimeOffset.UtcNow,"PC",1,results,[]);
        Assert.True(run.Complete);Assert.Equal("Normal",BenchmarkScoring.Rating(run.Score));
        Assert.False(run with{Cancelled=true} is {Complete:true});
        Assert.False(run with{Results=[results[0]]} is {Complete:true});
        Assert.Null(BenchmarkScoring.Combined([Result("CPU",double.NaN),Result("GPU 3D",0)]));
    }

    [Fact]
    public void NewProfilesAndGroupedBenchmarkResultsSurviveSaving()
    {
        var directory=Path.Combine(Path.GetTempPath(),"absolute-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new PreferenceStore(Path.Combine(directory,"preferences.json"));var preferences=new AppPreferences();
            preferences.GameProfiles["fortnite"]=new(null,"Normal");
            preferences.BenchmarkRuns.Add(new("run",DateTimeOffset.UtcNow,"PC",1,[new("CPU",1024,"MB/s",4,DateTimeOffset.UtcNow,"hashing")],["GPU unavailable"]));
            store.Save(preferences);var restored=store.Load();
            Assert.Equal("Normal",restored.GameProfiles["fortnite"].Priority);Assert.Null(restored.GameProfiles["fortnite"].PowerPlan);
            var run=Assert.Single(restored.BenchmarkRuns);Assert.Single(run.Results);Assert.Single(run.Notices);Assert.False(run.Complete);
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    [Fact]
    public void FortniteUsesTheLiveSeasonBackgroundBeforeShopPromotions()
    {
        using var json=JsonDocument.Parse("""
        {"dynamicbackgrounds":{"backgrounds":{"backgrounds":[{"stage":"default","backgroundimage":"https://cdn2.unrealengine.com/current-season.jpg"}]}},"battleroyalenewsv2":{"news":{"motds":[{"title":"Item Shop Bear","image":"https://cdn2.unrealengine.com/bear.jpg"}]}}}
        """);
        Assert.Equal("https://cdn2.unrealengine.com/current-season.jpg",PublisherImages.Fortnite(json.RootElement));
    }

    [Fact]
    public void FortniteNewsFallbackUsesRecentSeasonArt()
    {
        using var json=JsonDocument.Parse("""
        {"news":[{"title":"Old season","date":"2025-10-01","image":"https://cdn2.unrealengine.com/old.jpg"},{"title":"New season","date":"2026-10-01","image":"https://cdn2.unrealengine.com/new.jpg"}]}
        """);
        Assert.Equal("https://cdn2.unrealengine.com/new.jpg",PublisherImages.Fortnite(json.RootElement));
    }

    [Fact]
    public void ValorantUsesTheCurrentOfficialArticleInsteadOfTheSiteLogo()
    {
        Assert.Equal("/en-us/news/game-updates/valorant-patch-notes-11-10/",PublisherImages.LatestRiotArticle("<a href=\"/en-us/news/game-updates/valorant-patch-notes-11-10/\">Latest</a>","valorant"));
        Assert.Null(PublisherImages.LatestRiotArticle("<meta property=\"og:image\" content=\"red-logo.jpg\">","valorant"));
    }
}
