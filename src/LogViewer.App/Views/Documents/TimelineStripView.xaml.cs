using System.Windows.Controls;

namespace LogViewer.App.Views.Documents;

/// <summary>The volume timeline strip at the top of <see cref="TailDocumentView"/>; bound to its <c>TailDocumentViewModel</c>.</summary>
public partial class TimelineStripView : UserControl
{
    public TimelineStripView()
    {
        InitializeComponent();
    }
}
