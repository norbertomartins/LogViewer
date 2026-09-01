using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenEtwTailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _provider = string.Empty;

    [ObservableProperty]
    private string _level = "Informational";

    // ETW trace levels are a byte where higher = more detail. 1..5 are the standard names; "Debug"
    // is an alias for "capture everything" (Verbose plus any provider-defined level above 5).
    private static readonly (string Name, int Value)[] Levels =
    [
        ("Critical", 1),
        ("Error", 2),
        ("Warning", 3),
        ("Informational", 4),
        ("Verbose", 5),
        ("Debug", 0xFF),
    ];

    public IReadOnlyList<string> LevelOptions { get; } = Levels.Select(l => l.Name).ToArray();

    public IReadOnlyList<string> Presets { get; } =
    [
        "Microsoft-Windows-DotNETRuntime",
        "Microsoft-Windows-Kernel-Process",
        "Microsoft-Windows-Kernel-Network",
        "Microsoft-Windows-DNS-Client",
        "Microsoft-Windows-WinINet",
    ];

    public int LevelValue
    {
        get
        {
            foreach (var (name, value) in Levels)
            {
                if (name == Level)
                {
                    return value;
                }
            }

            return 4;
        }
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(Provider);
}
