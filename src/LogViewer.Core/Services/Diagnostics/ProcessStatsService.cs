using System.Diagnostics;

namespace LogViewer.Core.Services.Diagnostics;

public sealed record ProcessStatsSnapshot(double WorkingSetMb, double CpuPercent, double LinesPerSecond);

/// <summary>
/// Samples the current process's RAM and CPU usage plus an externally supplied cumulative line
/// count, deriving lines/sec between samples. Intended to be polled roughly once a second and
/// displayed in the title bar.
/// </summary>
public sealed class ProcessStatsService : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleUtc;
    private long _lastLineCount;
    private bool _hasSample;

    public ProcessStatsSnapshot Sample(long totalLinesProcessed)
    {
        _process.Refresh();
        var now = DateTime.UtcNow;

        if (!_hasSample)
        {
            _lastCpuTime = _process.TotalProcessorTime;
            _lastSampleUtc = now;
            _lastLineCount = totalLinesProcessed;
            _hasSample = true;
            return new ProcessStatsSnapshot(_process.WorkingSet64 / (1024.0 * 1024.0), 0, 0);
        }

        var elapsedSeconds = Math.Max((now - _lastSampleUtc).TotalSeconds, 0.001);
        var cpuTime = _process.TotalProcessorTime;
        var cpuDeltaSeconds = (cpuTime - _lastCpuTime).TotalSeconds;
        var cpuPercent = cpuDeltaSeconds / (Environment.ProcessorCount * elapsedSeconds) * 100.0;

        var lineDelta = Math.Max(totalLinesProcessed - _lastLineCount, 0);
        var linesPerSecond = lineDelta / elapsedSeconds;

        _lastCpuTime = cpuTime;
        _lastSampleUtc = now;
        _lastLineCount = totalLinesProcessed;

        return new ProcessStatsSnapshot(_process.WorkingSet64 / (1024.0 * 1024.0), Math.Max(cpuPercent, 0), linesPerSecond);
    }

    public void Dispose() => _process.Dispose();
}
