using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Absolute.App.Controls;
using Absolute.App.Services;
using Absolute.Core;
using static Absolute.App.Controls.Ui;

namespace Absolute.App;

public partial class MainWindow
{
    private void BuildPresets()
    {
        var selected=Games.FirstOrDefault(g=>g.Id==preferences.SelectedGame);
        var active=Games.FirstOrDefault(g=>g.Id==gamePresets.ActiveGame);
        var auto=new CheckBox{Content="Auto detect",IsChecked=preferences.AutoGamePresets,VerticalAlignment=VerticalAlignment.Center};
        auto.Click+=async(_,_)=>await Safe(async()=>{preferences.AutoGamePresets=auto.IsChecked==true;store.Save(preferences);if(!preferences.AutoGamePresets){gameMonitor.Stop();await gamePresets.RestoreAsync();gameState="Automatic detection paused";}else{gameMonitor.Start();await DetectGameAsync();}RefreshPage("Presets");});
        var summary=Stack(Text("Selected preset",11,"#A396AF"),Text(selected?.Name??"Choose a game",25,weight:FontWeights.SemiBold),Space(6),Text(active is null?gameState:$"{active.Name} · {gameState}",12,"#B69ACA"));
        Page.Children.Add(Card(Columns((summary,1),(auto,-1))));
        var grid=new UniformGrid{Columns=3};
        var search=Search(gameQuery,"Search games",value=>{gameQuery=value;RenderGames();});
        var add=B("Add game",()=>{var game=AddGameDialog.Ask(this);if(game is not null){preferences.CustomGames.Add(game);preferences.SelectedGame=game.Id;store.Save(preferences);Navigate("Presets");}});
        add.Margin=new Thickness(14,0,0,0);var toolbar=Columns((search,1),(add,-1));toolbar.Margin=new Thickness(0,0,0,20);Page.Children.Add(toolbar);
        Page.Children.Add(grid);
        Page.Children.Add(Text("Automatic profiles use an available High performance power plan and Above Normal game-process priority. They restore when the game or ABSOLUTE closes. Protected games may restrict priority changes. In-game files stay untouched.",12,"#92849F"));
        void RenderGames()
        {
            grid.Children.Clear();
            foreach(var game in Games.Where(g=>g.Name.Contains(gameQuery,StringComparison.OrdinalIgnoreCase)))
            {
                var chosen=preferences.SelectedGame==game.Id;
                var button=B(chosen?"Selected":"Select preset",()=>{preferences.SelectedGame=game.Id;store.Save(preferences);Navigate("Presets");},!chosen);
                button.IsEnabled=!chosen;button.HorizontalAlignment=HorizontalAlignment.Stretch;
                var body=Stack(Text("Power plan · Process priority",11,"#A99AB7"),Space(13),button);body.Margin=new Thickness(16);
                var card=new Border{Background=Brush("#15101C"),BorderBrush=Brush(chosen?game.Accent:"#302538"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=Artwork.CardMargin(grid.Children.Count),Child=Stack(Artwork.Banner(game.Name,game.Accent,game.Banner),body)};
                grid.Children.Add(card);
            }
        }
        RenderGames();
    }

    private void BuildPlaybooks()
    {
        var active=Catalogs.Playbooks.FirstOrDefault(p=>p.Id==PlaybookService.Active);
        var choices=new ComboBox{MinWidth=210,Margin=new Thickness(0,0,12,0)};
        foreach(var profile in Catalogs.Playbooks.Where(p=>p.Website is null))choices.Items.Add(new ComboBoxItem{Content=profile.Name,Tag=profile.Id});
        choices.SelectedIndex=Math.Max(0,Catalogs.Playbooks.Where(p=>p.Website is null).ToList().FindIndex(p=>p.Id==active?.Id));
        var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(choices);
        controls.Children.Add(B("Activate",async()=>
        {
            if(choices.SelectedItem is not ComboBoxItem item)return;
            var id=(string)item.Tag;
            if(!Confirm("Activate settings playbook?","Windows will request administrator access and create a restore point first. Only the listed Windows settings change. Previous ABSOLUTE playbook settings are restored before this profile applies."))return;
            await PlaybookService.ActivateAsync(id);Notify("Settings playbook activated.");Navigate("Playbooks");
        },true));
        var restore=B("Restore settings",async()=>{await PlaybookService.ActivateAsync("restore");Notify("Playbook settings restored.");Navigate("Playbooks");});restore.Margin=new Thickness(10,0,0,0);controls.Children.Add(restore);
        var summary=Stack(Text("Active playbook",11,"#A396AF"),Text(active?.Name??"Windows defaults / no profile",25,weight:FontWeights.SemiBold),Space(18),controls);
        Page.Children.Add(Card(summary));
        Page.Children.Add(Text("ABSOLUTE profiles change reversible settings only. External OS projects can replace Windows components or files, so their cards open the official project. System Restore is never used to switch profiles.",12,"#A396AF"));
        Section("Playbooks & Windows projects");
        var grid=new UniformGrid{Columns=3};Page.Children.Add(grid);
        foreach(var project in Catalogs.Playbooks)
        {
            var detail=Text(project.Detail,12,"#B3A3C0");detail.Height=38;
            var button=project.Website is string url?B("Official project  ↗",()=>OpenUrl(url)):B("Choose profile",()=>{choices.SelectedItem=choices.Items.Cast<ComboBoxItem>().First(i=>(string)i.Tag==project.Id);PageScroll.ScrollToTop();});
            var body=Stack(Text(project.Kind,10,"#A488BC"),Space(8),detail,Space(12),button);body.Margin=new Thickness(16);
            grid.Children.Add(new Border{Background=Brush("#15101C"),BorderBrush=Brush("#302538"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=Artwork.CardMargin(grid.Children.Count),Child=Stack(Artwork.Banner(project.Name,project.Accent,project.Banner,true),body)});
        }
        Page.Children.Add(B("System protection",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"SystemPropertiesProtection.exe")){UseShellExecute=true})));
    }
}
