using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Shell;

public partial class MdiHostView : UserControl
{
    public MdiHostView()
    {
        InitializeComponent();
    }

    private void OnCascadeClick(object sender, RoutedEventArgs e)
    {
        var documents = GetDocuments();
        const double offset = 28;
        const double width = 480;
        const double height = 320;

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            document.IsMdiMaximized = false;
            document.MdiLeft = 12 + (i % 10) * offset;
            document.MdiTop = 12 + (i % 10) * offset;
            document.MdiWidth = width;
            document.MdiHeight = height;
            document.MdiZIndex = i + 1;
        }
    }

    private void OnTileHorizontalClick(object sender, RoutedEventArgs e) => TileEvenly(stackVertically: false);

    private void OnTileVerticalClick(object sender, RoutedEventArgs e) => TileEvenly(stackVertically: true);

    private void TileEvenly(bool stackVertically)
    {
        var documents = GetDocuments();
        if (documents.Count == 0)
        {
            return;
        }

        var viewportWidth = Math.Max(MdiScrollViewer.ViewportWidth, 400);
        var viewportHeight = Math.Max(MdiScrollViewer.ViewportHeight, 300);

        var slice = stackVertically ? viewportHeight / documents.Count : viewportWidth / documents.Count;

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            document.IsMdiMaximized = false;

            if (stackVertically)
            {
                document.MdiLeft = 0;
                document.MdiTop = i * slice;
                document.MdiWidth = viewportWidth;
                document.MdiHeight = slice;
            }
            else
            {
                document.MdiLeft = i * slice;
                document.MdiTop = 0;
                document.MdiWidth = slice;
                document.MdiHeight = viewportHeight;
            }
        }
    }

    private IReadOnlyList<TailDocumentViewModel> GetDocuments() =>
        DataContext is MainViewModel viewModel ? viewModel.Documents.ToList() : [];
}
