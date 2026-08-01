namespace LogViewer.Core.Services.ServiceControl;

public sealed record WindowsServiceInfo(string ServiceName, string DisplayName, string Status, string StartType);
