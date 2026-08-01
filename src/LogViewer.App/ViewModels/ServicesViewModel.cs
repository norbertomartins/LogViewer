using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.Services.ServiceControl;

namespace LogViewer.App.ViewModels;

public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly ServiceControlService _serviceControl = new();

    [ObservableProperty]
    private WindowsServiceInfo? _selectedService;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<WindowsServiceInfo> Services { get; } = [];

    public ServicesViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            IsBusy = true;
            var selectedName = SelectedService?.ServiceName;
            Services.Clear();
            foreach (var service in _serviceControl.ListServices())
            {
                Services.Add(service);
            }

            SelectedService = Services.FirstOrDefault(s => s.ServiceName == selectedName);
            StatusMessage = $"{Services.Count} services listed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to list services: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task StartServiceAsync()
    {
        return SelectedService is null
            ? Task.CompletedTask
            : RunActionAsync(() => _serviceControl.Start(SelectedService.ServiceName), $"Starting '{SelectedService.DisplayName}'...");
    }

    [RelayCommand]
    private Task StopServiceAsync()
    {
        return SelectedService is null
            ? Task.CompletedTask
            : RunActionAsync(() => _serviceControl.Stop(SelectedService.ServiceName), $"Stopping '{SelectedService.DisplayName}'...");
    }

    private async Task RunActionAsync(Action action, string busyMessage)
    {
        try
        {
            IsBusy = true;
            StatusMessage = busyMessage;
            await Task.Run(action);
            StatusMessage = "Done.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message} (service control usually requires running LogViewer as administrator).";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }
}
