using System.Windows;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public partial class ResetConfirmationWindow : Window
{
    public ResetConfirmationWindow(WidgetViewModel model)
    {
        InitializeComponent();
        MessageText.Text = Localization.Current[model.PendingReset ? "RetryConfirm" : "ResetConfirm"];
        AvailabilityText.Text = model.ResetAvailability;
        ExpiryText.Text = model.ResetExpiry;
        if (model.PendingReset) ConfirmButton.Content = Localization.Current["RetryReset"];
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
