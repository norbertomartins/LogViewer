using System.Collections.ObjectModel;
using AvalonDock;
using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;

namespace LogViewer.App.Services;

/// <summary>
/// Hosts the open <see cref="TailDocumentViewModel"/>s for all three window modes. Tabbed and
/// Floating are backed by a single AvalonDock <see cref="DockingManager"/> — they aren't separate
/// hosts in AvalonDock's model, just a state (docked vs. floating) of the same
/// <see cref="LayoutDocument"/>s — so switching between them floats or docks every attached document
/// rather than tearing anything down. MDI has no shared chrome with AvalonDock, so it's rendered by a
/// separate view (<c>MdiHostView</c>) reading the same <see cref="Documents"/> collection; switching
/// to/from MDI only toggles which view is visible; it never recreates a document view-model.
/// </summary>
public sealed partial class DockingWindowModeHost : ObservableObject, IWindowModeHost
{
    private DockingManager? _dockingManager;

    [ObservableProperty]
    private WindowModeKind _mode = WindowModeKind.Tabbed;

    public bool IsMdiMode => Mode == WindowModeKind.Mdi;

    public ObservableCollection<TailDocumentViewModel> Documents { get; } = [];

    partial void OnModeChanged(WindowModeKind value) => OnPropertyChanged(nameof(IsMdiMode));

    /// <summary>Wires this host to the actual AvalonDock control instance once the view is loaded.</summary>
    public void AttachDockingManager(DockingManager dockingManager) => _dockingManager = dockingManager;

    public void SwitchMode(WindowModeKind mode)
    {
        Mode = mode;

        if (mode == WindowModeKind.Mdi || _dockingManager?.Layout is null)
        {
            return;
        }

        foreach (var document in _dockingManager.Layout.Descendents().OfType<LayoutDocument>().ToList())
        {
            if (mode == WindowModeKind.Floating)
            {
                if (!document.IsFloating)
                {
                    document.Float();
                }
            }
            else if (document.IsFloating)
            {
                document.Dock();
            }
        }
    }
}
