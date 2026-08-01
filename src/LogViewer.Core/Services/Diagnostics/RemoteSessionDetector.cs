using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LogViewer.Core.Services.Diagnostics;

/// <summary>
/// Detects whether the process is running in a Remote Desktop session, so the UI layer can widen its
/// redraw-batching interval — RDP's own remoting protocol amortizes bitmap updates, so sending fewer,
/// larger UI updates reduces perceived lag rather than adding to it.
/// </summary>
public static class RemoteSessionDetector
{
    private const int SM_REMOTESESSION = 0x1000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [SupportedOSPlatform("windows")]
    public static bool IsRemoteSession => GetSystemMetrics(SM_REMOTESESSION) != 0;

    /// <summary>Pure helper (no P/Invoke) so the redraw-batching decision is unit-testable without a real RDP session.</summary>
    public static TimeSpan EffectiveRefreshInterval(TimeSpan configured, bool isRemoteSession)
    {
        var minimum = TimeSpan.FromMilliseconds(250);
        return isRemoteSession && configured < minimum ? minimum : configured;
    }
}
