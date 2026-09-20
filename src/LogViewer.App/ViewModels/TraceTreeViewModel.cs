using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal SerilogTracing "Trace Tree" panel: lists every distinct trace found in the
/// document's currently buffered lines, and renders the selected trace's spans as a parent/child tree
/// (via <see cref="SerilogTraceTreeBuilder"/>), same shape as a completed request's call graph. Modeled
/// on <see cref="DocumentStatsViewModel"/> — an on-demand snapshot over the document's own buffer, not a
/// live subscription.
/// </summary>
public sealed partial class TraceTreeViewModel : ObservableObject
{
    private readonly TailDocumentViewModel _document;

    public TraceTreeViewModel(TailDocumentViewModel document, string? initialTraceId)
    {
        _document = document;
        Refresh(initialTraceId);
    }

    public string Title => _document.Title;

    public ObservableCollection<TraceSummary> Traces { get; } = [];

    public ObservableCollection<TraceSpanNode> SpanTree { get; } = [];

    [ObservableProperty]
    private TraceSummary? _selectedTrace;

    [ObservableProperty]
    private string? _statusMessage;

    partial void OnSelectedTraceChanged(TraceSummary? value) => RebuildTree(value?.TraceId);

    [RelayCommand]
    private void Refresh() => Refresh(SelectedTrace?.TraceId);

    private void Refresh(string? preferredTraceId)
    {
        var lines = BufferedEvents();

        Traces.Clear();
        foreach (var summary in SerilogTraceTreeBuilder.SummarizeTraces(lines))
        {
            Traces.Add(summary);
        }

        StatusMessage = Traces.Count == 0 ? Loc.Get("Vm_TraceTree_NoTraces") : null;

        var target = preferredTraceId is not null
            ? Traces.FirstOrDefault(t => t.TraceId == preferredTraceId)
            : null;
        SelectedTrace = target ?? Traces.FirstOrDefault();
    }

    private void RebuildTree(string? traceId)
    {
        SpanTree.Clear();
        if (traceId is null)
        {
            return;
        }

        foreach (var node in SerilogTraceTreeBuilder.BuildTree(BufferedEvents(), traceId))
        {
            SpanTree.Add(node);
        }
    }

    private IReadOnlyList<(long LineNumber, StructuredLogEvent Event)> BufferedEvents() =>
        _document.Lines
            .Where(l => l.Structured is not null)
            .Select(l => (l.LineNumber, l.Structured!))
            .ToList();

    [RelayCommand]
    private void JumpToSpan(TraceSpanNode? node)
    {
        if (node is not null)
        {
            _document.TryNavigateToLineNumber(node.LineNumber);
        }
    }
}
