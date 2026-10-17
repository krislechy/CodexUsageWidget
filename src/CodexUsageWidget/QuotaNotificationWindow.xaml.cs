using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexUsageWidget.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

/// <summary>Unowned, non-activating notification that remains usable while the widget is hidden.</summary>
public partial class QuotaNotificationWindow : Window
{
    private readonly Action _openWidget;
    private readonly DispatcherTimer _dismiss;
    private readonly DispatcherTimer _activity;
    private uint? _inputAtArrival;
    internal Func<uint?> InputTimestamp { private get; set; } = ReadLastInput;
    private readonly List<string> _messages = [];
    private HwndSource? _source;
    private Forms.Screen? _previewScreen;
    private string _headingKey = "QuotaNotificationTitle";
    internal bool DismissalScheduled => _dismiss.IsEnabled;
    internal bool PreventsActivation => !ShowActivated && (GetWindowLongPtr(new WindowInteropHelper(this).Handle, -20).ToInt64() & 0x08000000L) != 0;

    public QuotaNotificationWindow(Action openWidget, TimeSpan? displayDuration = null)
    {
        _openWidget = openWidget;
        InitializeComponent();
        _dismiss = new DispatcherTimer { Interval = displayDuration ?? TimeSpan.FromSeconds(2) };
        _dismiss.Tick += (_, _) => { if (!IsMouseOver && !IsKeyboardFocusWithin) Close(); };
        _activity = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _activity.Tick += (_, _) =>
        {
            var input = InputTimestamp();
            if (input.HasValue && input == _inputAtArrival) return;
            _activity.Stop();
            RestartDismissal();
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            _ = SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000L | 0x00000080L)); // NOACTIVATE | TOOLWINDOW
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowMessage);
        };
        Loaded += (_, _) => Place();
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
        Localization.Current.PropertyChanged += LanguageChanged;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Closed += (_, _) =>
        {
            _dismiss.Stop();
            _activity.Stop();
            _source?.RemoveHook(WindowMessage);
            Localization.Current.PropertyChanged -= LanguageChanged;
            SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        };
    }

    public void ShowMessages(string headingKey, IEnumerable<string> messages)
    {
        if (_messages.Count > 0 && _headingKey != headingKey) headingKey = "QuotaNotificationTitle";
        _headingKey = headingKey;
        foreach (var message in messages)
            if (!_messages.Contains(message, StringComparer.Ordinal)) _messages.Add(message);
        if (_messages.Count > 8) _messages.RemoveRange(0, _messages.Count - 8);
        NotificationHeading.Text = Localization.Current[_headingKey];
        NotificationBody.Text = string.Join("\n\n", _messages);
        if (!IsVisible) Show();
        UpdateLayout(); Place();
        _dismiss.Stop();
        _inputAtArrival = InputTimestamp();
        if (_inputAtArrival.HasValue) _activity.Start();
        else { _activity.Stop(); RestartDismissal(); }
        UIElementAutomationPeer.CreatePeerForElement(NotificationBody)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotificationHeading.Text = Localization.Current[_headingKey];
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Place);
    }
    private void Place() => WindowPlacement.AnchorNotification(this, _previewScreen);
    internal void PlaceForPreview(Forms.Screen screen) { _previewScreen = screen; Place(); }
    private void RestartDismissal() { _dismiss.Stop(); if (!_activity.IsEnabled && !IsMouseOver && !IsKeyboardFocusWithin) _dismiss.Start(); }
    private void PauseDismissal(object sender, MouseEventArgs e) => _dismiss.Stop();
    private void ResumeDismissal(object sender, MouseEventArgs e) => RestartDismissal();
    private void Dismiss_Click(object sender, RoutedEventArgs e) { e.Handled = true; Close(); }
    private void Notification_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source != NotificationSurface)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar or System.Windows.Controls.Primitives.Thumb) return;
            source = source is FrameworkContentElement content ? content.Parent : System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        e.Handled = true;
        Close();
        _openWidget();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // WM_MOUSEACTIVATE: MA_NOACTIVATE
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    private static uint? ReadLastInput()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Tick : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Tick; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
