using System.Windows;
using LogViewer.App.Models;
using LogViewer.App.ViewModels;
using LogViewer.App.Views.Dialogs;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Search;
using Microsoft.Win32;

namespace LogViewer.App.Services;

public sealed class DialogService(ThemeService themeService) : IDialogService
{
    public IReadOnlyList<string>? ShowOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            Multiselect = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    public IReadOnlyList<string>? ShowOpenMergedSourcesDialog()
    {
        var viewModel = new OpenMergedSourcesViewModel();
        var window = new OpenMergedSourcesView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true ? viewModel.ResolveFiles() : null;
    }

    public bool ShowHighlightPresetEditor(ICollection<HighlightPreset> presets)
    {
        var viewModel = new HighlightPresetEditorViewModel(presets);
        var window = new HighlightPresetEditorView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        presets.Clear();
        foreach (var preset in viewModel.ToPresets())
        {
            presets.Add(preset);
        }

        return true;
    }

    public bool ShowExternalToolEditor(ICollection<ExternalToolDefinition> tools, IReadOnlyList<HighlightRule> availableHighlightRules)
    {
        var viewModel = new ExternalToolEditorViewModel(tools, availableHighlightRules);
        var window = new ExternalToolEditorView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        tools.Clear();
        foreach (var tool in viewModel.ToDefinitions())
        {
            tools.Add(tool);
        }

        return true;
    }

    public bool ShowSettings(AppSettings settings)
    {
        var viewModel = new SettingsViewModel(settings, this);
        var window = new SettingsView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.ApplyTo(settings);
        themeService.Apply(themeService.ResolveActiveTheme(settings));
        return true;
    }

    public bool ShowThemeManager(AppSettings settings)
    {
        var viewModel = new ThemeManagerViewModel(settings);
        var window = new ThemeManagerView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        settings.CustomThemes.Clear();
        settings.CustomThemes.AddRange(viewModel.ToCustomThemes());
        settings.ActiveThemeId = viewModel.ActiveThemeId;
        return true;
    }

    public DirectoryWatchSelection? ShowOpenDirectoryWatchDialog(string? initialDirectoryPath = null)
    {
        var viewModel = new OpenDirectoryWatchViewModel(initialDirectoryPath);
        var window = new OpenDirectoryWatchView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true
            ? new DirectoryWatchSelection(viewModel.DirectoryPath, viewModel.Pattern, viewModel.AutoSwitchToLatestFile)
            : null;
    }

    public EventLogSelection? ShowOpenEventLogDialog()
    {
        var viewModel = new OpenEventLogViewModel();
        var window = new OpenEventLogView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return null;
        }

        var filters = viewModel.Filters.Select(f => f.ToRule()).ToList();
        return new EventLogSelection(viewModel.ChannelName, filters);
    }

    public HttpTailSelection? ShowOpenHttpTailDialog()
    {
        var viewModel = new OpenHttpTailViewModel();
        var window = new OpenHttpTailView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true
            ? new HttpTailSelection(viewModel.Url.Trim(), viewModel.Mode, viewModel.HeaderLines)
            : null;
    }

    public ProcessTailSelection? ShowOpenProcessTailDialog()
    {
        var vm = new OpenProcessTailViewModel();
        var window = new OpenProcessTailView { DataContext = vm, Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true
            ? new ProcessTailSelection(vm.FileName.Trim(), vm.Arguments.Trim(), vm.RestartOnExit)
            : null;
    }

    public SshTailSelection? ShowOpenSshTailDialog()
    {
        var vm = new OpenSshTailViewModel();
        var window = new OpenSshTailView { DataContext = vm, Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true
            ? new SshTailSelection(
                vm.Host.Trim(), vm.Port, vm.Username.Trim(),
                string.IsNullOrEmpty(vm.Password) ? null : vm.Password,
                string.IsNullOrWhiteSpace(vm.PrivateKeyPath) ? null : vm.PrivateKeyPath.Trim(),
                string.IsNullOrEmpty(vm.PrivateKeyPassphrase) ? null : vm.PrivateKeyPassphrase,
                vm.Command.Trim(),
                string.IsNullOrWhiteSpace(vm.HostKeyFingerprintSha256) ? null : vm.HostKeyFingerprintSha256.Trim(),
                vm.AcceptAnyHostKey)
            : null;
    }

    public EtwTailSelection? ShowOpenEtwTailDialog()
    {
        var vm = new OpenEtwTailViewModel();
        var window = new OpenEtwTailView { DataContext = vm, Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true
            ? new EtwTailSelection(vm.Provider.Trim(), vm.LevelValue)
            : null;
    }

    public PaletteCommand? ShowCommandPalette(IReadOnlyList<PaletteCommand> commands)
    {
        var window = new CommandPaletteView
        {
            DataContext = new CommandPaletteViewModel(commands),
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true ? window.ChosenCommand : null;
    }

    public void ShowServicesDialog()
    {
        var window = new ServicesView
        {
            DataContext = new ServicesViewModel(),
            Owner = Application.Current?.MainWindow,
        };

        window.Show();
    }

    public void ShowSearchDialog(TailDocumentViewModel document, IFullTextSearchService fileSearchService, IEventLogSearchService eventLogSearchService)
    {
        var window = new SearchView
        {
            DataContext = new SearchViewModel(document, fileSearchService, eventLogSearchService),
            Owner = Application.Current?.MainWindow,
        };

        window.Show();
    }

    public void ShowSimilarBlockDialog(
        TailDocumentViewModel sourceDocument,
        LogLineViewModel anchorLine,
        IReadOnlyList<TailDocumentViewModel> openDocuments,
        ISimilarBlockFinder blockFinder)
    {
        var window = new SimilarBlockView
        {
            DataContext = new SimilarBlockViewModel(sourceDocument, anchorLine, openDocuments, blockFinder, this),
            Owner = Application.Current?.MainWindow,
        };

        window.Show();
    }

    public bool ShowCustomizeDialog(TailDocumentViewModel document)
    {
        var viewModel = new DocumentCustomizeViewModel(document.CustomColorHex, document.CustomIconGlyph);
        var window = new DocumentCustomizeView
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return false;
        }

        document.CustomColorHex = viewModel.SelectedColorHex;
        document.CustomIconGlyph = viewModel.SelectedIconGlyph;
        return true;
    }
}
