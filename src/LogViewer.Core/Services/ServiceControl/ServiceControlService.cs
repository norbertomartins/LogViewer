using System.Runtime.Versioning;
using System.ServiceProcess;

namespace LogViewer.Core.Services.ServiceControl;

/// <summary>
/// Lists and starts/stops Windows services via <see cref="ServiceController"/>. Listing works for any
/// user; starting/stopping a given service requires the permissions granted to it (in practice this
/// usually means running elevated, which the caller surfaces to the user via the thrown exception
/// rather than this service silently failing).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceControlService
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public IReadOnlyList<WindowsServiceInfo> ListServices() =>
        ServiceController.GetServices()
            .Select(ToInfo)
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Start(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
    }

    public void Stop(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
        {
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, WaitTimeout);
    }

    private static WindowsServiceInfo ToInfo(ServiceController controller)
    {
        var startType = "Unknown";
        try
        {
            startType = controller.StartType.ToString();
        }
        catch (InvalidOperationException)
        {
            // Some services (drivers, certain protected services) don't expose start type to non-admins.
        }

        return new WindowsServiceInfo(controller.ServiceName, controller.DisplayName, controller.Status.ToString(), startType);
    }
}
