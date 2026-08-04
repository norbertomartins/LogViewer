using System.Windows;
using System.Windows.Controls;

namespace LogViewer.App.Controls;

/// <summary>Small swatch button that opens a palette grid for picking a "#RRGGBB" color without typing hex.</summary>
public partial class ColorPickerButton : UserControl
{
    public static readonly DependencyProperty SelectedHexProperty =
        DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorPickerButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsPopupOpenProperty =
        DependencyProperty.Register(nameof(IsPopupOpen), typeof(bool), typeof(ColorPickerButton),
            new PropertyMetadata(false));

    /// <summary>Curated grid instead of full RGB: grayscale plus light/base/dark shades of common accent hues.</summary>
    public static readonly string[] Palette =
    [
        "#FFFFFF", "#F5F5F5", "#E0E0E0", "#BDBDBD", "#9E9E9E", "#757575", "#616161", "#424242", "#212121", "#000000",
        "#FFCDD2", "#FFE0B2", "#FFF9C4", "#DCEDC8", "#B2EBF2", "#BBDEFB", "#C5CAE9", "#E1BEE7", "#F8BBD0", "#D7CCC8",
        "#EF5350", "#FFA726", "#FFEE58", "#9CCC65", "#26C6DA", "#42A5F5", "#5C6BC0", "#AB47BC", "#EC407A", "#8D6E63",
        "#B71C1C", "#E65100", "#F57F17", "#33691E", "#00838F", "#0D47A1", "#283593", "#4A148C", "#880E4F", "#3E2723",
    ];

    public ColorPickerButton()
    {
        InitializeComponent();
    }

    public string? SelectedHex
    {
        get => (string?)GetValue(SelectedHexProperty);
        set => SetValue(SelectedHexProperty, value);
    }

    public bool IsPopupOpen
    {
        get => (bool)GetValue(IsPopupOpenProperty);
        set => SetValue(IsPopupOpenProperty, value);
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            SelectedHex = hex;
        }

        IsPopupOpen = false;
    }
}
