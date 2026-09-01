using System.Windows;
using System.Windows.Input;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class CommandPaletteView : Window
{
    public CommandPaletteView()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
        Deactivated += (_, _) => CloseWithResult(accepted: false);
    }

    /// <summary>Set to the chosen command when the user accepts; null when cancelled.</summary>
    public PaletteCommand? ChosenCommand { get; private set; }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ViewModel?.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel?.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                CloseWithResult(accepted: true);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseWithResult(accepted: false);
                e.Handled = true;
                break;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e) => CloseWithResult(accepted: true);

    private void CloseWithResult(bool accepted)
    {
        if (!IsLoaded)
        {
            return;
        }

        ChosenCommand = accepted ? ViewModel?.Selected : null;
        DialogResult = accepted && ChosenCommand is not null;
    }
}
