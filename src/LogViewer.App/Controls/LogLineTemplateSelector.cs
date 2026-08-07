using System.Windows;
using System.Windows.Controls;
using LogViewer.App.Models;

namespace LogViewer.App.Controls;

/// <summary>Chooses between the plain-text and structured-log row templates per item, based on whether
/// <see cref="LogLineViewModel.Structured"/> parsed successfully — so a document in structured view still
/// falls back to the plain row for any line that isn't valid Serilog JSON (blank lines, malformed JSON,
/// content from before structured view was toggled on, etc.).</summary>
public sealed class LogLineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PlainTemplate { get; set; }

    public DataTemplate? StructuredTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is LogLineViewModel { Structured: not null } ? StructuredTemplate ?? PlainTemplate : PlainTemplate;
}
