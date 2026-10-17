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
        var auto=new CheckBox{Content="Auto detect",IsChecked=preferences.AutoGamePresets,VerticalAlignment=VerticalAlignment.Top};
        auto.Click+=async(_,_)=>await Safe(async()=>
        {
            preferences.AutoGamePresets=auto.IsChecked==true;store.Save(preferences);
            if(!preferences.AutoGamePresets){gameMonitor.Stop();await gamePresets.RestoreAsync();gameState="Automatic detection paused";}
            else{gameMonitor.Start();await DetectGameAsync();}
            RefreshPage("Presets");
        });
        var summary=Stack(Text("Selected preset",11,"#C7B9D5"),Space(8),Text(selected?.Name??"Choose a game",34,weight:FontWeights.SemiBold),Space(10),Text(active is null?gameState:$"{active.Name} · {gameState}",13,"#C8AFDE"));
        var hero=Artwork.Scene(selected??Catalogs.Games[0],240,true);
        var heroContent=Columns((summary,1),(auto,-1));heroContent.Margin=new Thickness(28);heroContent.VerticalAlignment=VerticalAlignment.Bottom;hero.Children.Add(heroContent);
        Page.Children.Add(new Border{Child=hero,CornerRadius=new CornerRadius(13),Margin=new Thickness(0,0,0,22)});
        var grid=new UniformGrid{Columns=3};
        var search=Search(gameQuery,"Search games",value=>{gameQuery=value;RenderGames();});
        var add=B("Add game",()=>{var game=AddGameDialog.Ask(this);if(game is not null){preferences.CustomGames.Add(game);preferences.SelectedGame=game.Id;store.Save(preferences);Navigate("Presets");}});
        add.Margin=new Thickness(14,0,0,0);var toolbar=Columns((search,1),(add,-1));toolbar.Margin=new Thickness(0,0,0,20);Page.Children.Add(toolbar);Page.Children.Add(grid);
        void RenderGames()
        {
            grid.Children.Clear();
            foreach(var game in Games.Where(g=>g.Name.Contains(gameQuery,StringComparison.OrdinalIgnoreCase)))
            {
                var chosen=preferences.SelectedGame==game.Id;
                var scene=Artwork.Scene(game,150);var name=Text(game.Name,19,weight:FontWeights.SemiBold);name.Margin=new Thickness(16,0,14,16);name.VerticalAlignment=VerticalAlignment.Bottom;scene.Children.Add(name);
                var button=B(chosen?"Selected":"Select",()=>{preferences.SelectedGame=game.Id;store.Save(preferences);Navigate("Presets");},!chosen);
                button.IsEnabled=!chosen;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(14);
                grid.Children.Add(new Border{Background=Brush("#15101C"),BorderBrush=Brush(chosen?game.Accent:"#2B2335"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=Artwork.CardMargin(grid.Children.Count),Child=Stack(scene,button)});
            }
        }
        RenderGames();
    }

    private void BuildPlaybooks()
    {
        var grid=new UniformGrid{Columns=2};Page.Children.Add(grid);
        foreach(var project in Catalogs.Playbooks)
        {
            var initials=Text(project.Name switch{"AtlasOS"=>"A","ReviOS"=>"R","AME 11"=>"AME","FSOSX"=>"FS","tiny11builder"=>"11",_=>"M"},project.Id=="ame11"?13:23,project.Accent,FontWeights.SemiBold);
            initials.HorizontalAlignment=HorizontalAlignment.Center;initials.VerticalAlignment=VerticalAlignment.Center;
            var mark=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(12),Background=Brush("#221A2C"),Child=initials,Margin=new Thickness(0,0,15,0)};
            var heading=Columns((mark,-1),(Stack(Text(project.Name,24,weight:FontWeights.SemiBold),Text(project.Maker,12,"#A693B7")),1));
            var description=Text(project.Detail,13,"#B7A9C3");description.MinHeight=66;description.Margin=new Thickness(0,18,0,16);
            var button=B("Official project  ↗",()=>OpenUrl(project.Website!));button.HorizontalAlignment=HorizontalAlignment.Left;
            var card=Card(Stack(heading,description,button),new Thickness(24));card.Margin=new Thickness(grid.Children.Count%2==0?0:9,0,grid.Children.Count%2==0?9:0,18);grid.Children.Add(card);
        }
    }
}
