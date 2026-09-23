namespace LogViewer.App.Tests.TestUtilities;

/// <summary>Pumps the WPF dispatcher until <paramref name="condition"/> holds (or 5s pass), so tailed lines
/// queued by <c>UiDispatcherLineSink</c> reach the document view-model inside a test.</summary>
public static class TestDispatcher
{
    public static void SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }
}
