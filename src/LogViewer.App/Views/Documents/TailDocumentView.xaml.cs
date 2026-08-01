using System.Windows;
using System.Windows.Controls;
using LogViewer.App.Models;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Documents;

public partial class TailDocumentView : UserControl
{
    private TailDocumentViewModel? _viewModel;

    public TailDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested -= OnScrollToLineRequested;
        }

        _viewModel = e.NewValue as TailDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
        }
    }

    private void OnScrollToEndRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LineListView.Items.Count > 0)
            {
                LineListView.ScrollIntoView(LineListView.Items[^1]);
            }
        });
    }

    private void OnScrollToLineRequested(LogLineViewModel line)
    {
        Dispatcher.BeginInvoke(() => LineListView.ScrollIntoView(line));
    }
}
