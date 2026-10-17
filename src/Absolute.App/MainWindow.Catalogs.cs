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
        if(editingGame is string id&&Games.FirstOrDefault(g=>g.Id==id) is {} game){BuildGameEditor(game);return;}
        var selected=Games.FirstOrDefault(g=>g.Id==preferences.SelectedGame);
        var active=Games.FirstOrDefault(g=>g.Id==gamePresets.ActiveGame);
        var summary=Stack(Text(active is null?"Game profiles":"Playing now",11,"#C7B9D5"),Space(8),Text(active?.Name??selected?.Name??"Ready when you play",32,weight:FontWeights.SemiBold),Space(10),Text(active is null?"Automatic game detection":gameState,13,"#C8AFDE"));
        var hero=Artwork.Scene(active??selected??Catalogs.Games[0],220,true);
        summary.Margin=new Thickness(26);summary.VerticalAlignment=VerticalAlignment.Bottom;hero.Children.Add(summary);
        Page.Children.Add(new Border{Child=hero,CornerRadius=new CornerRadius(13),Margin=new Thickness(0,0,0,22)});
        var grid=new UniformGrid{Columns=4};
        var search=Search(gameQuery,"Search games",value=>{gameQuery=value;RenderGames();});
        var add=B("Add game",()=>{var custom=AddGameDialog.Ask(this);if(custom is not null){preferences.CustomGames.Add(custom);store.Save(preferences);editingGame=custom.Id;Navigate("Presets");}});
        add.Margin=new Thickness(14,0,0,0);var toolbar=Columns((search,1),(add,-1));toolbar.Margin=new Thickness(0,0,0,20);Page.Children.Add(toolbar);Page.Children.Add(grid);
        void RenderGames()
        {
            grid.Children.Clear();
            foreach(var entry in Games.Where(g=>g.Name.Contains(gameQuery,StringComparison.OrdinalIgnoreCase)))
            {
                var scene=Artwork.Scene(entry,116);var name=Text(entry.Name,15,weight:FontWeights.SemiBold);name.Margin=new Thickness(13,0,10,12);name.VerticalAlignment=VerticalAlignment.Bottom;scene.Children.Add(name);
                var button=new Button{Content=scene,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,BorderThickness=new Thickness(0),Background=Brush("#15101C"),Cursor=System.Windows.Input.Cursors.Hand,Margin=new Thickness(0,0,12,14)};
                System.Windows.Automation.AutomationProperties.SetName(button,"Configure "+entry.Name);
                button.Click+=(_,_)=>{editingGame=entry.Id;Navigate("Presets");};grid.Children.Add(button);
            }
        }
        RenderGames();
    }

    private void BuildGameEditor(GameDefinition game)
    {
        var back=B("‹  Games",()=>{editingGame=null;Navigate("Presets");});back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new Thickness(0,0,0,18);Page.Children.Add(back);
        var hero=Artwork.Scene(game,210,true);var name=Text(game.Name,32,weight:FontWeights.SemiBold);name.Margin=new Thickness(26);name.VerticalAlignment=VerticalAlignment.Bottom;hero.Children.Add(name);Page.Children.Add(hero);Page.Children.Add(Space(22));
        var profile=preferences.GameProfiles.GetValueOrDefault(game.Id)??GameProfileSettings.Default;
        var power=new ComboBox{MinWidth=240,HorizontalAlignment=HorizontalAlignment.Stretch};
        power.Items.Add(new ComboBoxItem{Content="Keep current",Tag=(Guid?)null});
        foreach(var plan in powerPlans)power.Items.Add(new ComboBoxItem{Content=plan.Name,Tag=plan.Id});
        power.SelectedItem=power.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,profile.PowerPlan))??power.Items[0];
        var priority=new ComboBox{MinWidth=240,HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var (label,value) in new[]{("Keep current","Keep"),("Normal","Normal"),("Above normal","AboveNormal")})priority.Items.Add(new ComboBoxItem{Content=label,Tag=value});
        priority.SelectedItem=priority.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,profile.Priority))??priority.Items[0];
        var save=B("Save profile",async()=>
        {
            preferences.GameProfiles[game.Id]=new((power.SelectedItem as ComboBoxItem)?.Tag as Guid?,(priority.SelectedItem as ComboBoxItem)?.Tag as string??"Keep");
            preferences.SelectedGame=game.Id;store.Save(preferences);
            if(gamePresets.ActiveGame==game.Id)await gamePresets.RestoreAsync();
            await DetectGameAsync();Notify(game.Name+" profile saved");
        },true);save.HorizontalAlignment=HorizontalAlignment.Left;save.Margin=new Thickness(0,20,0,0);
        Page.Children.Add(Card(Stack(Row("System","Power plan","While the game is running",power),Row("Processor","Process priority","Applied when Windows permits",priority,true),save)));
        Page.Children.Add(Text("Detected automatically · Previous settings restored when the game closes",12,"#A396AF"));
    }

    private void BuildPlaybooks()
    {
        var grid=new UniformGrid{Columns=2};Page.Children.Add(grid);
        foreach(var project in Catalogs.Playbooks)
        {
            var mark=Artwork.ProjectAvatar(project);
            mark.Margin=new Thickness(0,0,15,0);
            var heading=Columns((mark,-1),(Stack(Text(project.Name,23,weight:FontWeights.SemiBold),Text(project.Maker,12,"#A693B7")),1));
            var facts=new StackPanel{Margin=new Thickness(0,17,0,18)};
            foreach(var fact in project.Facts??[])facts.Children.Add(new TextBlock{Text="•  "+fact,FontSize=12,Foreground=Brush("#B7A9C3"),Margin=new Thickness(0,0,0,7),TextWrapping=TextWrapping.Wrap});
            var button=B("Official project  ↗",()=>OpenUrl(project.Website!));button.HorizontalAlignment=HorizontalAlignment.Left;
            var card=Card(Stack(heading,facts,button),new Thickness(24));card.Margin=new Thickness(grid.Children.Count%2==0?0:9,0,grid.Children.Count%2==0?9:0,18);grid.Children.Add(card);
        }
    }
}
