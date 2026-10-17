using System.IO;
using System.Windows;
using System.Windows.Controls;
using Absolute.Core;
using Microsoft.Win32;
using static Absolute.App.Controls.Ui;

namespace Absolute.App.Controls;

internal static class AddGameDialog
{
    public static GameDefinition? Ask(Window owner)
    {
        GameDefinition? game=null;string? executable=null;
        var dialog=new Window{Title="Add game",Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=480,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#15101D"),FontFamily=owner.FontFamily,ShowInTaskbar=false};
        var name=new TextBox{Margin=new Thickness(0,8,0,16)};System.Windows.Automation.AutomationProperties.SetName(name,"Game name");
        var path=Text("Choose the game's executable",12,"#A396AF");path.Margin=new Thickness(0,12,0,18);
        var browse=new Button{Content="Browse .exe"};browse.Click+=(_,_)=>{var picker=new OpenFileDialog{Filter="Game executable (*.exe)|*.exe",CheckFileExists=true};if(picker.ShowDialog(dialog)==true){executable=picker.FileName;path.Text=executable;if(string.IsNullOrWhiteSpace(name.Text))name.Text=Path.GetFileNameWithoutExtension(executable);}};
        var error=Text("",12,"#CB97AA");
        var save=new Button{Content="Add game",Style=(Style)Application.Current.FindResource("Primary")};save.Click+=(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)||executable is null){error.Text="Enter a name and choose an executable.";return;}
            game=new("custom-"+Guid.NewGuid().ToString("N"),name.Text.Trim(),[Path.GetFileNameWithoutExtension(executable)],"#AB85D2",13,executable);dialog.DialogResult=true;
        };
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,10,0)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};actions.Children.Add(cancel);actions.Children.Add(save);
        dialog.Content=new Border{Padding=new Thickness(26),BorderBrush=Brush("#574066"),BorderThickness=new Thickness(1),Child=Stack(Text("Add game",23,weight:FontWeights.SemiBold),Text("Name",12,"#A396AF"),name,browse,path,error,actions)};
        dialog.Loaded+=(_,_)=>name.Focus();dialog.ShowDialog();return game;
    }
}
