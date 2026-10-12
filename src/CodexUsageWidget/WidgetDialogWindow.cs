using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexUsageWidget;

/// <summary>Shared frameless dialog behavior. Startup placement is declared in XAML.</summary>
public class WidgetDialogWindow : Window
{
    public WidgetDialogWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
    }

    protected void DialogClose_Click(object sender, RoutedEventArgs e) => Close();

    protected void DialogHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return;
            current = VisualTreeHelper.GetParent(current);
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
