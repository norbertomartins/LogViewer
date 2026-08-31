using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenEtwTailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _provider = string.Empty;

    [ObservableProperty]
    private string _level = "Informational";

    public IReadOnlyList<string> LevelOptions { get; } = ["Critical", "Error", "Warning", "Informational", "Verbose"];

    public IReadOnlyList<string> Presets { get; } =
    [
        "Microsoft-Windows-DotNETRuntime",
        "Microsoft-Windows-Kernel-Process",
        "Microsoft-Windows-Kernel-Network",
        "Microsoft-Windows-DNS-Client",
        "Microsoft-Windows-WinINet",
    ];

    public int LevelValue => Array.IndexOf(new[] { "Critical", "Error", "Warning", "Informational", "Verbose" }, Level) is var i and >= 0 ? i + 1 : 4;

    public bool IsValid => !string.IsNullOrWhiteSpace(Provider);
}
