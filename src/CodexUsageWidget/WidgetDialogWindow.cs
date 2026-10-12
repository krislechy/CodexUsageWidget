using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

/// <summary>Shared frameless dialog behavior, centered on its owner and kept on screen.</summary>
public class WidgetDialogWindow : Window
{
    public WidgetDialogWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ContentRendered += (_, _) => CenterOnOwner();
    }

    private void CenterOnOwner()
    {
        if (Owner is null) return;
        var dpi = VisualTreeHelper.GetDpi(Owner);
        var ownerCenter = new Point(Owner.Left + Owner.ActualWidth / 2, Owner.Top + Owner.ActualHeight / 2);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)(ownerCenter.X * dpi.DpiScaleX), (int)(ownerCenter.Y * dpi.DpiScaleY)));
        var area = screen.WorkingArea;
        Left = Math.Clamp(ownerCenter.X - ActualWidth / 2, area.Left / dpi.DpiScaleX,
            Math.Max(area.Left / dpi.DpiScaleX, area.Right / dpi.DpiScaleX - ActualWidth));
        Top = Math.Clamp(ownerCenter.Y - ActualHeight / 2, area.Top / dpi.DpiScaleY,
            Math.Max(area.Top / dpi.DpiScaleY, area.Bottom / dpi.DpiScaleY - ActualHeight));
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
