using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenDirectoryWatchViewModel : ObservableObject
{
    [ObservableProperty]
    private string _directoryPath = string.Empty;

    [ObservableProperty]
    private string _pattern = "*.log";

    [ObservableProperty]
    private bool _autoSwitchToLatestFile = true;

    public OpenDirectoryWatchViewModel(string? initialDirectoryPath = null)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectoryPath))
        {
            _directoryPath = initialDirectoryPath;
        }
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Select a directory to watch" };
        if (dialog.ShowDialog() == true)
        {
            DirectoryPath = dialog.FolderName;
        }
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(DirectoryPath) && !string.IsNullOrWhiteSpace(Pattern);
}
