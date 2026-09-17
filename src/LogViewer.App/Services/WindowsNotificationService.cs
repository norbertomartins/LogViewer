using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace LogViewer.App.Services;

/// <summary>
/// Surfaces alerts as a tray balloon tip via the app's existing <see cref="TaskbarIcon"/> (the same
/// control minimize-to-tray uses) — no new dependency. The icon is normally <c>Collapsed</c> unless the
/// main window is minimized to the tray (see <c>MainWindow.OnMinimizeToTrayClick</c>), so a notification
/// temporarily makes it <c>Visible</c> long enough for the balloon to show, then restores the prior
/// visibility — it never interferes with the actual minimize-to-tray flow, since that always leaves the
/// icon <c>Visible</c> until the user brings the window back.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromSeconds(8);

    private TaskbarIcon? _trayIcon;
    private DispatcherTimer? _restoreTimer;

    public void Attach(TaskbarIcon trayIcon) => _trayIcon = trayIcon;

    public void Notify(string title, string message)
    {
        if (_trayIcon is null)
        {
            return;
        }

        try
        {
            var wasVisible = _trayIcon.Visibility == Visibility.Visible;
            _trayIcon.Visibility = Visibility.Visible;
            _trayIcon.ShowBalloonTip(title, message, BalloonIcon.Warning);

            _restoreTimer?.Stop();
            if (!wasVisible)
            {
                _restoreTimer = new DispatcherTimer { Interval = RestoreDelay };
                _restoreTimer.Tick += (_, _) =>
                {
                    _restoreTimer!.Stop();
                    if (_trayIcon is not null)
                    {
                        _trayIcon.Visibility = Visibility.Collapsed;
                    }
                };
                _restoreTimer.Start();
            }
        }
        catch (Exception)
        {
            // Best-effort alert — a tray/shell hiccup must never break log tailing.
        }
    }
}
