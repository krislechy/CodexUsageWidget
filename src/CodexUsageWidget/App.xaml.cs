using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace CodexUsageWidget;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.Contains("--demo") || e.Args.Contains("--capture");
        if (!preview)
        {
            _instance = new Mutex(true, @"Local\CodexUsageWidget", out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("Виджет уже запущен. Откройте его через значок рядом с часами Windows.", "Codex Usage Widget");
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
