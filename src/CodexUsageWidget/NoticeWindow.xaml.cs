namespace CodexUsageWidget;

public partial class NoticeWindow : WidgetDialogWindow
{
    public NoticeWindow(string? messageKey = null)
    {
        InitializeComponent();
        if (messageKey is not null) NoticeText.Text = Core.Localization.Current[messageKey];
    }
}
