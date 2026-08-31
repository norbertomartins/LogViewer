using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenProcessTailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private bool _restartOnExit = true;

    /// <summary>Common ready-made commands offered as a starting point.</summary>
    public IReadOnlyList<string> Presets { get; } =
    [
        "journalctl -f -o cat",
        "docker logs -f <container>",
        "kubectl logs -f <pod>",
        "adb logcat",
        "wsl journalctl -f -o cat",
    ];

    public bool IsValid => !string.IsNullOrWhiteSpace(FileName);

    public void ApplyPreset(string preset)
    {
        var parts = preset.Split(' ', 2);
        FileName = parts[0];
        Arguments = parts.Length > 1 ? parts[1] : string.Empty;
    }
}
