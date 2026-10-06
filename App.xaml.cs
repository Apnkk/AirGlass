using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AirGlass.Services;

namespace AirGlass;

/// <summary>
/// Entry point: single instance (a second launch just reveals the first window),
/// crash logging, optional hidden start (--minimized) for Windows autostart.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\AirGlass.SingleInstance";
    private const string ShowEventName = @"Local\AirGlass.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (Exception ex)
            {
                AppLog.Error("Impossible de réveiller l'instance existante", ex);
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var waiter = new Thread(WaitForShowRequests) { IsBackground = true, Name = "ShowWindowWaiter" };
        waiter.Start();

        var window = new MainWindow();
        MainWindow = window;
        window.Begin(startHidden: e.Args.Contains("--minimized"));
    }

    private void WaitForShowRequests()
    {
        try
        {
            while (_showEvent is not null && _showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            }
        }
        catch (ObjectDisposedException)
        {
            // app is exiting
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Erreur non gérée", e.Exception);
        MessageBox.Show(
            "Une erreur inattendue est survenue :\n" + e.Exception.Message
                + "\n\nDétails dans " + AppLog.FilePath,
            "AirGlass", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _showEvent?.Dispose(); } catch { /* ignore */ }
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        try { _mutex?.Dispose(); } catch { /* ignore */ }
        base.OnExit(e);
    }
}
