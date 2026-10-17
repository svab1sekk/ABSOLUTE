using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Absolute.App.Controls;

internal static class Ui
{
    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    public static TextBlock Text(string text, double size = 14, string color = "#F2EFF7", FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static Border Card(UIElement content, Thickness? padding = null) => new()
    {
        Background = Brush("#141019"), BorderBrush = Brush("#2B2335"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(13), Padding = padding ?? new Thickness(23), Child = content, Margin = new Thickness(0, 0, 0, 18)
    };

    public static Grid Columns(params (UIElement Element, double Width)[] items)
    {
        var grid = new Grid();
        for (var i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = items[i].Width < 0 ? GridLength.Auto : new GridLength(items[i].Width, GridUnitType.Star) });
            Grid.SetColumn(items[i].Element, i);
            grid.Children.Add(items[i].Element);
        }
        return grid;
    }

    public static Button Button(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        if (primary) button.Style = (Style)Application.Current.FindResource("Primary");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    public static FrameworkElement Icon(string name, double size = 20, string color = "#A89AB9")
    {
        var data = name switch
        {
            "Home" => "M 2,11 L 12,2 L 22,11 M 5,9 V 22 H 10 V 15 H 15 V 22 H 20 V 9",
            "Drivers" => "M 5,2 H 19 V 22 H 5 Z M 9,6 H 15 M 9,10 H 15 M 9,15 H 15 V 19 H 9 Z M 2,7 H 5 M 19,7 H 22 M 2,17 H 5 M 19,17 H 22",
            "System" => "M 5,5 H 19 V 19 H 5 Z M 9,9 H 15 V 15 H 9 Z M 9,1 V 5 M 15,1 V 5 M 9,19 V 23 M 15,19 V 23 M 1,9 H 5 M 1,15 H 5 M 19,9 H 23 M 19,15 H 23",
            "Network" => "M 12,4 V 11 M 4,16 V 12 H 20 V 16 M 1,16 H 7 V 22 H 1 Z M 9,16 H 15 V 22 H 9 Z M 17,16 H 23 V 22 H 17 Z M 9,1 H 15 V 6 H 9 Z",
            "Presets" => "M 3,6 H 21 M 3,12 H 21 M 3,18 H 21 M 8,3 V 9 M 16,9 V 15 M 10,15 V 21",
            "Playbooks" => "M 5,3 H 15 L 20,8 V 22 H 5 Z M 15,3 V 8 H 20 M 9,12 H 16 M 9,16 H 14",
            "Benchmark" => "M 3,19 A 10,10 0 1 1 21,19 M 12,15 L 17,8 M 5,15 H 7 M 6,8 L 8,10 M 12,5 V 7 M 17,16 H 19",
            "PC" => "M 2,3 H 22 V 17 H 2 Z M 12,17 V 22 M 7,22 H 17",
            "Settings" => "M 9,2 H 15 L 16,5 L 19,6 L 22,8 L 20,12 L 22,16 L 19,18 L 16,19 L 15,22 H 9 L 8,19 L 5,18 L 2,16 L 4,12 L 2,8 L 5,6 L 8,5 Z M 16,12 A 4,4 0 1 1 8,12 A 4,4 0 1 1 16,12",
            "Arrow" => "M 5,12 H 19 M 13,6 L 19,12 L 13,18",
            _ => "M 5,12 L 10,17 L 20,6"
        };
        var path = new Path { Data = Geometry.Parse(data), Stroke = Brush(color), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        return new Viewbox { Width = size, Height = size, Child = path, Stretch = Stretch.Uniform };
    }

    public static Border Pill(string label, string color = "#BBA0D8") => new()
    {
        Child = Text(label, 11, color), CornerRadius = new CornerRadius(6), Background = Brush("#23192E"),
        Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
    };

    public static FrameworkElement Row(string icon, string title, string detail, UIElement action, bool last = false)
    {
        var glyph = new Border { Background = Brush("#21182C"), CornerRadius = new CornerRadius(10), Width = 42, Height = 42, Child = Icon(icon), Margin = new Thickness(0, 0, 15, 0) };
        var labels = Stack(Text(title, 14, weight: FontWeights.SemiBold), Text(detail, 12, "#A39AAF"));
        labels.Margin = new Thickness(0, 0, 16, 0);
        return new Border
        {
            Child = Columns((glyph, -1), (labels, 1), (action, -1)), Padding = new Thickness(0, 17, 0, 17),
            BorderBrush = Brush("#292230"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1)
        };
    }

    public static TextBox Search(string text,string hint,Action<string> changed)
    {
        var box=new TextBox{Text=text,Tag=hint,ToolTip=hint,MinWidth=190,VerticalContentAlignment=VerticalAlignment.Center};
        System.Windows.Automation.AutomationProperties.SetName(box,hint);
        box.TextChanged+=(_,_)=>changed(box.Text);return box;
    }
}
