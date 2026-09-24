using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.Controls;

/// <summary>One color of the picker. The swatch lists are shared by every picker (only one popup is open at a time), so
/// <see cref="IsSelected"/> is refreshed each time a popup opens.</summary>
public sealed partial class ColorSwatch : ObservableObject
{
    public ColorSwatch(string hex)
    {
        Hex = hex;
        ColorPalette.TryParse(hex, out var r, out var g, out var b);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        Brush = brush;
    }

    public string Hex { get; }

    public Brush Brush { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Small swatch button that opens a palette for picking a "#RRGGBB" color without typing hex: 40 suggested shades, the
/// 216 web-safe colors, the user's recent custom colors, and an editor (saturation/brightness square, hue bar, hex box)
/// for any other color. Custom colors are shared app-wide (<see cref="CustomColors"/>); the app persists them.
/// </summary>
public partial class ColorPickerButton : UserControl
{
    public static readonly DependencyProperty SelectedHexProperty =
        DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorPickerButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsPopupOpenProperty =
        DependencyProperty.Register(nameof(IsPopupOpen), typeof(bool), typeof(ColorPickerButton),
            new PropertyMetadata(false));

    public static IReadOnlyList<ColorSwatch> SuggestedSwatches { get; } = [.. ColorPalette.Suggested.Select(h => new ColorSwatch(h))];

    public static IReadOnlyList<ColorSwatch> WebSwatches { get; } = [.. ColorPalette.WebSafe.Select(h => new ColorSwatch(h))];

    public static ObservableCollection<ColorSwatch> CustomSwatches { get; } = [];

    /// <summary>Raised after the custom colors change (a new one was used), for the app to persist them.</summary>
    public static event Action? CustomColorsChanged;

    /// <summary>The remembered custom colors, most recent first.</summary>
    public static IReadOnlyList<string> CustomColors => [.. CustomSwatches.Select(s => s.Hex)];

    /// <summary>Replaces the remembered custom colors (invalid entries and duplicates dropped), e.g. from settings.</summary>
    public static void LoadCustomColors(IEnumerable<string> colors)
    {
        var list = new List<string>();
        foreach (var hex in colors.Reverse())
        {
            ColorPalette.Remember(list, hex);
        }

        CustomSwatches.Clear();
        foreach (var hex in list)
        {
            CustomSwatches.Add(new ColorSwatch(hex));
        }
    }

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updatingHexBox;

    public ColorPickerButton()
    {
        InitializeComponent();
        SvArea.SizeChanged += (_, _) => UpdateEditorVisuals(updateHexBox: false);
        HueBar.SizeChanged += (_, _) => UpdateEditorVisuals(updateHexBox: false);
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

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        var selected = ColorPalette.NormalizeHex(SelectedHex);
        foreach (var swatch in SuggestedSwatches.Concat(WebSwatches).Concat(CustomSwatches))
        {
            swatch.IsSelected = swatch.Hex == selected;
        }

        NoCustomColorsHint.Visibility = CustomSwatches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorToggle.IsChecked = false;
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColorSwatch swatch })
        {
            SelectedHex = swatch.Hex;
        }

        IsPopupOpen = false;
    }

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsPopupOpen = false;
            Toggle.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && HexBox.IsKeyboardFocusWithin)
        {
            OnUseCustomColor(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>Starts the editor from the current color (or a mid red when there's none).</summary>
    private void OnEditorOpened(object sender, RoutedEventArgs e)
    {
        if (ColorPalette.TryParse(SelectedHex, out var r, out var g, out var b))
        {
            (_hue, _saturation, _value) = ColorPalette.ToHsv(r, g, b);
        }
        else
        {
            (_hue, _saturation, _value) = (0, 0.75, 0.9);
        }

        UpdateEditorVisuals(updateHexBox: true);
        HexBox.Focus();
        HexBox.SelectAll();
    }

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        PickSaturationAndValue(e.GetPosition(SvArea));
        e.Handled = true;
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured)
        {
            PickSaturationAndValue(e.GetPosition(SvArea));
        }
    }

    private void PickSaturationAndValue(Point point)
    {
        _saturation = Math.Clamp(point.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(point.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
        UpdateEditorVisuals(updateHexBox: true);
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        PickHue(e.GetPosition(HueBar));
        e.Handled = true;
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured)
        {
            PickHue(e.GetPosition(HueBar));
        }
    }

    private void PickHue(Point point)
    {
        _hue = Math.Clamp(point.X / Math.Max(1, HueBar.ActualWidth), 0, 1) * 359.99;
        UpdateEditorVisuals(updateHexBox: true);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingHexBox || !ColorPalette.TryParse(HexBox.Text, out var r, out var g, out var b))
        {
            return;
        }

        var (hue, saturation, value) = ColorPalette.ToHsv(r, g, b);

        // A gray has no hue of its own: keep the bar where it is.
        _hue = saturation > 0 ? hue : _hue;
        _saturation = saturation;
        _value = value;
        UpdateEditorVisuals(updateHexBox: false);
    }

    private void UpdateEditorVisuals(bool updateHexBox)
    {
        var (hr, hg, hb) = ColorPalette.FromHsv(_hue, 1, 1);
        HueFill.Fill = new SolidColorBrush(Color.FromRgb(hr, hg, hb));

        var (r, g, b) = ColorPalette.FromHsv(_hue, _saturation, _value);
        Preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));

        Canvas.SetLeft(SvThumb, (_saturation * SvArea.ActualWidth) - (SvThumb.Width / 2));
        Canvas.SetTop(SvThumb, ((1 - _value) * SvArea.ActualHeight) - (SvThumb.Height / 2));
        Canvas.SetLeft(HueThumb, (_hue / 360 * HueBar.ActualWidth) - (HueThumb.Width / 2));

        if (updateHexBox)
        {
            _updatingHexBox = true;
            HexBox.Text = ColorPalette.ToHex(r, g, b);
            _updatingHexBox = false;
        }
    }

    private void OnUseCustomColor(object sender, RoutedEventArgs e)
    {
        var hex = ColorPalette.NormalizeHex(HexBox.Text);
        if (hex is null)
        {
            var (r, g, b) = ColorPalette.FromHsv(_hue, _saturation, _value);
            hex = ColorPalette.ToHex(r, g, b);
        }

        RememberCustomColor(hex);
        SelectedHex = hex;
        IsPopupOpen = false;
    }

    private static void RememberCustomColor(string hex)
    {
        var colors = CustomColors.ToList();
        ColorPalette.Remember(colors, hex);
        if (colors.SequenceEqual(CustomColors))
        {
            return;
        }

        LoadCustomColors(colors);
        CustomColorsChanged?.Invoke();
    }
}
