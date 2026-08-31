using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenHttpTailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url = "https://";

    [ObservableProperty]
    private string _mode = "Auto";

    /// <summary>Optional request headers, one <c>Name: Value</c> per line (e.g. <c>Authorization: Bearer …</c>).</summary>
    [ObservableProperty]
    private string _headers = string.Empty;

    public IReadOnlyList<string> ModeOptions { get; } = ["Auto", "Stream", "Poll"];

    public OpenHttpTailViewModel(string? url = null, string? mode = null, IEnumerable<string>? headers = null)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            _url = url;
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            _mode = mode;
        }

        if (headers is not null)
        {
            _headers = string.Join(Environment.NewLine, headers);
        }
    }

    public IReadOnlyList<string> HeaderLines =>
        Headers.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l.Contains(':')).ToList();

    public bool IsValid => Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "ws" or "wss";
}
