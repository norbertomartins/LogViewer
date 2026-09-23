using System.Windows;
using System.Windows.Threading;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class FileBrowserView : Window
{
    private FileBrowserViewModel? _viewModel;

    public FileBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.ScrollToIndexRequested -= OnScrollToIndexRequested;
            }

            _viewModel = e.NewValue as FileBrowserViewModel;
            if (_viewModel is not null)
            {
                _viewModel.ScrollToIndexRequested += OnScrollToIndexRequested;
            }
        };
        Loaded += async (_, _) =>
        {
            if (_viewModel is not null)
            {
                await _viewModel.InitializeAsync();
            }
        };
        Closed += (_, _) => _viewModel?.Dispose();
    }

    private void OnScrollToIndexRequested(int index)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (index < 0 || index >= LineListView.Items.Count)
            {
                return;
            }

            LineListView.ScrollIntoView(LineListView.Items[index]);
            LineListView.Focus();
        }, DispatcherPriority.Background);
    }
}
