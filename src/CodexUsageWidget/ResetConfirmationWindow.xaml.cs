using System.Windows;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public partial class ResetConfirmationWindow : WidgetDialogWindow
{
    public ResetConfirmationWindow(WidgetViewModel model, ResetEntry? selected = null)
    {
        InitializeComponent();
        MessageText.Text = Localization.Current[model.PendingReset ? "RetryConfirm" : "ResetConfirm"];
        AvailabilityText.Text = selected is null ? model.ResetAvailability : $"{selected.Title}\n{model.ResetAvailability}";
        ExpiryText.Text = selected?.Expiry ?? (model.PendingReset ? Localization.Current["ExpiryUnknown"] : model.ResetExpiry);
        if (model.PendingReset) ConfirmButton.Content = Localization.Current["RetryReset"];
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
