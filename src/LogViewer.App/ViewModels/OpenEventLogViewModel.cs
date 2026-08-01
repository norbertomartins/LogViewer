using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenEventLogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _channelName = "Application";

    [ObservableProperty]
    private EventLogFilterRuleViewModel? _selectedFilter;

    public ObservableCollection<EventLogFilterRuleViewModel> Filters { get; } = [];

    public IReadOnlyList<string> CommonChannels { get; } = ["Application", "System", "Setup"];

    public bool IsValid => !string.IsNullOrWhiteSpace(ChannelName);

    [RelayCommand]
    private void AddFilter()
    {
        var filter = new EventLogFilterRuleViewModel();
        Filters.Add(filter);
        SelectedFilter = filter;
    }

    [RelayCommand]
    private void RemoveFilter(EventLogFilterRuleViewModel? filter)
    {
        filter ??= SelectedFilter;
        if (filter is null)
        {
            return;
        }

        Filters.Remove(filter);
        SelectedFilter = Filters.FirstOrDefault();
    }
}
