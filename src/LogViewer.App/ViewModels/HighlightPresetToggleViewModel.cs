using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

/// <summary>Lightweight entry backing the toolbar's quick-toggle submenu — lets a preset be enabled/disabled
/// without opening the full preset editor.</summary>
public sealed partial class HighlightPresetToggleViewModel : ObservableObject
{
    public HighlightPresetToggleViewModel(Guid id, string name, bool isEnabled)
    {
        Id = id;
        Name = name;
        _isEnabled = isEnabled;
    }

    public Guid Id { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this, value);

    public event Action<HighlightPresetToggleViewModel, bool>? EnabledChanged;
}
