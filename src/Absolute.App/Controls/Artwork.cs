using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Absolute.App.Controls.Ui;

namespace Absolute.App.Controls;

internal static class Artwork
{
    private static readonly Dictionary<string,BitmapImage> Images=[];
    public static Border Banner(string title,string accent,int index,bool playbook=false)
    {
        var file=playbook?"playbook-banners.png":index>=16?"more-game-banners.png":"game-banners.png";
        if(!playbook)index%=16;
        if(!Images.TryGetValue(file,out var bitmap))
        {
            bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri("pack://application:,,,/Assets/"+file);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();Images[file]=bitmap;
        }
        var columns=playbook?3:4;var rows=4;
        var image=new ImageBrush(bitmap){Viewbox=new Rect(index%columns/(double)columns,index/columns/(double)rows,1d/columns,1d/rows),ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Stretch=Stretch.UniformToFill};
        var scene=new Grid{Background=image,Height=142,ClipToBounds=true};
        scene.SizeChanged+=(_,_)=>scene.Clip=new RectangleGeometry(new Rect(0,0,scene.ActualWidth,scene.ActualHeight),11,11);
        scene.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(0,8,6,12),Color.FromArgb(242,12,9,18),90)});
        var text=Text(title,20,weight:FontWeights.SemiBold);text.Margin=new Thickness(17,0,15,17);text.VerticalAlignment=VerticalAlignment.Bottom;scene.Children.Add(text);
        scene.Children.Add(new Border{Height=2,Background=Brush(accent),VerticalAlignment=VerticalAlignment.Bottom,Opacity=.75});
        return new Border{Child=scene,CornerRadius=new CornerRadius(11,11,0,0),ClipToBounds=true};
    }
    public static Thickness CardMargin(int index)=>(index%3) switch{0=>new Thickness(0,0,8,18),1=>new Thickness(4,0,4,18),_=>new Thickness(8,0,0,18)};
}
