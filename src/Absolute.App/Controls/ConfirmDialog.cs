using System.Windows;
using System.Windows.Controls;
using static Absolute.App.Controls.Ui;

namespace Absolute.App.Controls;

internal static class ConfirmDialog
{
    public static bool Ask(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height,
            Width = 440, Background = Brush("#15101D"), Foreground = Brush("#F2EFF7"),
            FontFamily = owner.FontFamily, ShowInTaskbar = false
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 10, 0) };
        var confirm = new Button { Content = "Confirm", MinWidth = 100, Style = (Style)Application.Current.FindResource("Primary") };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 28, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var heading = Text(title, 22, weight: FontWeights.SemiBold); heading.Margin = new Thickness(0, 0, 0, 14);
        dialog.Content = new Border { Child = Stack(heading, Text(message, 14, "#B6A8C5"), buttons), Padding = new Thickness(28), BorderBrush = Brush("#574066"), BorderThickness = new Thickness(1) };
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }
}
