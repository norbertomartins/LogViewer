using System.Collections.ObjectModel;
using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;

namespace LogViewer.App.Services;

/// <summary>
/// Hosts the set of open <see cref="TailDocumentViewModel"/>s and arranges them according to a
/// <see cref="WindowModeKind"/>. Switching mode rearranges the same documents — it never recreates
/// them — so scroll position, highlight state, bookmarks, and the live tail subscription all survive
/// a mode switch untouched.
/// </summary>
public interface IWindowModeHost
{
    WindowModeKind Mode { get; }

    ObservableCollection<TailDocumentViewModel> Documents { get; }

    void SwitchMode(WindowModeKind mode);
}
