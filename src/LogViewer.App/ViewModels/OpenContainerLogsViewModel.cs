using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.Core.Tailing;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the "Open Container Logs" dialog: pick Docker or Kubernetes, then a running container / pod (listed by asking
/// the CLI — <see cref="IContainerCli"/>), optionally a Kubernetes context, namespace and container, plus how much
/// history to start with. The result is a <see cref="ContainerLogRequest"/> that becomes a
/// <c>docker logs -f</c>/<c>kubectl logs -f</c> process tail (relaunched if it exits, restored with the session).
/// </summary>
public sealed partial class OpenContainerLogsViewModel : ObservableObject
{
    private readonly IContainerCli _cli;
    private CancellationTokenSource? _loadCts;

    public OpenContainerLogsViewModel(IContainerCli cli)
    {
        _cli = cli;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _isKubernetes;

    [ObservableProperty]
    private string? _context;

    [ObservableProperty]
    private string? _namespace;

    /// <summary>Container (Docker) or pod (Kubernetes) name — picked from <see cref="Targets"/> or typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _target;

    /// <summary>Optional container within the pod (Kubernetes); empty means the pod's default container.</summary>
    [ObservableProperty]
    private string? _container;

    [ObservableProperty]
    private int _tailLines = 500;

    [ObservableProperty]
    private bool _timestamps;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<string> Targets { get; } = [];

    public ObservableCollection<string> Namespaces { get; } = [];

    public ObservableCollection<string> Containers { get; } = [];

    public bool IsValid => ContainerLogs.IsSafeName(Target?.Trim());

    partial void OnIsKubernetesChanged(bool value) => _ = RefreshAsync();

    partial void OnTargetChanged(string? value)
    {
        Containers.Clear();
        Container = null;
        if (IsKubernetes && Targets.Contains(value ?? string.Empty))
        {
            _ = LoadContainersAsync(value!);
        }
    }

    /// <summary>Re-lists containers/pods (and namespaces for Kubernetes). CLI errors are shown, not thrown — the
    /// user can still type a name by hand.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        IsLoading = true;
        StatusMessage = Loc.Get("Vm_Containers_Loading");
        try
        {
            var runtime = IsKubernetes ? ContainerRuntime.Kubernetes : ContainerRuntime.Docker;
            var targets = await _cli.ListTargetsAsync(runtime, Blank(Namespace), Blank(Context), cts.Token);
            Replace(Targets, targets);
            if (IsKubernetes && Namespaces.Count == 0)
            {
                Replace(Namespaces, await _cli.ListNamespacesAsync(Blank(Context), cts.Token));
            }

            StatusMessage = targets.Count == 0 ? Loc.Get("Vm_Containers_NoneRunning") : null;
        }
        catch (ContainerCliException ex)
        {
            Targets.Clear();
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadContainersAsync(string pod)
    {
        try
        {
            Replace(Containers, await _cli.ListPodContainersAsync(pod, Blank(Namespace), Blank(Context), CancellationToken.None));
        }
        catch (ContainerCliException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    public ContainerLogRequest ToRequest() => new(
        IsKubernetes ? ContainerRuntime.Kubernetes : ContainerRuntime.Docker,
        Target!.Trim(),
        IsKubernetes ? Blank(Namespace) : null,
        IsKubernetes ? Blank(Context) : null,
        IsKubernetes ? Blank(Container) : null,
        TailLines,
        Timestamps);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
