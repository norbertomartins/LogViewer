using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LogViewer.Core.Tailing;

public enum ContainerRuntime
{
    Docker,
    Kubernetes,
}

/// <summary>What to follow: a Docker container, or a Kubernetes pod (optionally one container of it).</summary>
public sealed record ContainerLogRequest(
    ContainerRuntime Runtime,
    string Target,
    string? Namespace = null,
    string? Context = null,
    string? Container = null,
    int TailLines = 500,
    bool Timestamps = false);

/// <summary>A <c>docker</c>/<c>kubectl</c> invocation that failed or couldn't start (CLI not installed, cluster
/// unreachable …); the message is meant for the user.</summary>
public sealed class ContainerCliException(string message) : Exception(message);

/// <summary>Lists what can be followed — containers, pods, a pod's containers, namespaces — by asking the CLI.</summary>
public interface IContainerCli
{
    Task<IReadOnlyList<string>> ListTargetsAsync(ContainerRuntime runtime, string? @namespace, string? context, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListPodContainersAsync(string pod, string? @namespace, string? context, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListNamespacesAsync(string? context, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the <c>docker logs -f</c> / <c>kubectl logs -f</c> command that <see cref="ProcessTailSource"/> then tails
/// (and relaunches), and queries the CLIs for the pickers. Names are validated against the Docker/Kubernetes naming
/// rules before they go into an argument string, so a crafted name can't inject extra arguments.
/// </summary>
public static partial class ContainerLogs
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-/:@]*$")]
    private static partial Regex SafeNamePattern();

    public static bool IsSafeName(string? name) => !string.IsNullOrEmpty(name) && name.Length <= 253 && SafeNamePattern().IsMatch(name);

    public static (string FileName, string Arguments) BuildCommand(ContainerLogRequest request)
    {
        Require(request.Target, "target");
        var tail = Math.Clamp(request.TailLines, 0, 100_000);
        var timestamps = request.Timestamps ? " --timestamps" : string.Empty;

        if (request.Runtime == ContainerRuntime.Docker)
        {
            return ("docker", $"logs -f --tail {tail}{timestamps} {request.Target}");
        }

        var args = $"logs -f --tail={tail}{timestamps}";
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            Require(request.Context, "context");
            args += $" --context {request.Context}";
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            Require(request.Namespace, "namespace");
            args += $" -n {request.Namespace}";
        }

        args += $" {request.Target}";
        if (!string.IsNullOrWhiteSpace(request.Container))
        {
            Require(request.Container, "container");
            args += $" -c {request.Container}";
        }

        return ("kubectl", args);
    }

    /// <summary>Splits CLI output into names, one per line, dropping blanks and an optional <c>kind/</c> prefix.</summary>
    public static IReadOnlyList<string> ParseNames(string output, string? prefixToStrip = null) =>
        [.. output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => prefixToStrip is not null && n.StartsWith(prefixToStrip, StringComparison.Ordinal) ? n[prefixToStrip.Length..] : n)
            .Where(IsSafeName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static void Require(string? name, string what)
    {
        if (!IsSafeName(name))
        {
            throw new ArgumentException($"Invalid {what} name: '{name}'.");
        }
    }
}

/// <summary>Runs the real <c>docker</c>/<c>kubectl</c> executables (10 s timeout per query).</summary>
public sealed class ContainerCli : IContainerCli
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<string>> ListTargetsAsync(ContainerRuntime runtime, string? @namespace, string? context, CancellationToken cancellationToken)
    {
        if (runtime == ContainerRuntime.Docker)
        {
            return ContainerLogs.ParseNames(await RunAsync("docker", "ps --format {{.Names}}", cancellationToken).ConfigureAwait(false));
        }

        return ContainerLogs.ParseNames(
            await RunAsync("kubectl", $"get pods -o name{Scope(@namespace, context)}", cancellationToken).ConfigureAwait(false), "pod/");
    }

    public async Task<IReadOnlyList<string>> ListPodContainersAsync(string pod, string? @namespace, string? context, CancellationToken cancellationToken)
    {
        if (!ContainerLogs.IsSafeName(pod))
        {
            return [];
        }

        return ContainerLogs.ParseNames(await RunAsync(
            "kubectl", $"get pod {pod} -o jsonpath={{.spec.containers[*].name}}{Scope(@namespace, context)}", cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<string>> ListNamespacesAsync(string? context, CancellationToken cancellationToken) =>
        ContainerLogs.ParseNames(await RunAsync("kubectl", $"get namespaces -o name{Scope(null, context)}", cancellationToken).ConfigureAwait(false), "namespace/");

    private static string Scope(string? @namespace, string? context) =>
        (ContainerLogs.IsSafeName(@namespace) ? $" -n {@namespace}" : string.Empty)
        + (ContainerLogs.IsSafeName(context) ? $" --context {context}" : string.Empty);

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new ContainerCliException($"'{fileName}' was not found — is it installed and on PATH?");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new ContainerCliException($"'{fileName} {arguments}' did not answer within {Timeout.TotalSeconds:0}s.");
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr.ConfigureAwait(false)).Trim();
            throw new ContainerCliException(string.IsNullOrEmpty(error) ? $"'{fileName}' exited with code {process.ExitCode}." : error);
        }

        return await stdout.ConfigureAwait(false);
    }
}
