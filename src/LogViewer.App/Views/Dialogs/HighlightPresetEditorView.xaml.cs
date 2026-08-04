using System.IO;
using System.Text.Json;
using System.Windows;
using LogViewer.App.ViewModels;
using LogViewer.Core.Highlighting;
using Microsoft.Win32;

namespace LogViewer.App.Views.Dialogs;

public partial class HighlightPresetEditorView : Window
{
    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    public HighlightPresetEditorView()
    {
        InitializeComponent();
    }

    private HighlightPresetEditorViewModel Vm => (HighlightPresetEditorViewModel)DataContext;

    private void OnExportSelectedClick(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedPreset is null)
        {
            MessageBox.Show(this, "Select a preset to export first.", "Export Highlight Presets", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Export(Vm.ExportSelected(), Vm.SelectedPreset.Name);
    }

    private void OnExportAllClick(object sender, RoutedEventArgs e) => Export(Vm.ExportAll(), "highlight-presets");

    private void Export(HighlightPresetExportFile file, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Highlight presets (*.json)|*.json",
            FileName = $"{suggestedName}.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(file, ExportOptions));
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Highlight presets (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var file = JsonSerializer.Deserialize<HighlightPresetExportFile>(File.ReadAllText(dialog.FileName), ExportOptions);
            if (file is null || file.FormatVersion != HighlightPresetExportFile.CurrentFormatVersion)
            {
                throw new JsonException("Unrecognized file format.");
            }

            Vm.ImportFrom(file);
        }
        catch (JsonException)
        {
            MessageBox.Show(this, "That file isn't a valid highlight preset export.", "Import Highlight Presets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
