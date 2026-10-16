using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
        {
            Shutdown(await UpdateInstallation.RunHelperAsync(e.Args[1]));
            return;
        }
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, key) => { if (key.Key == Key.Tab) key.Handled = true; }), true);
        Localization.Current.SetLanguage(WidgetSettings.Load().Language);
        var preview = e.Args.Contains("--demo") || e.Args.Contains("--capture");
        if (!preview)
        {
            if (Semaphore.TryOpenExisting(@"Local\CodexUsageWidget.Update", out var updateLock))
            {
                using (updateLock)
                {
                    if (!updateLock.WaitOne(0))
                    {
                        new NoticeWindow("UpdateInProgress").ShowDialog(); Shutdown(); return;
                    }
                    updateLock.Release();
                }
            }
            _instance = new Mutex(true, @"Local\CodexUsageWidget", out _ownsMutex);
            if (!_ownsMutex)
            {
                new NoticeWindow().ShowDialog();
                Shutdown();
                return;
            }
        }
        var window = new MainWindow(preview, e.Args);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
