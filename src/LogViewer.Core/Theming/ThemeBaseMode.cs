namespace LogViewer.Core.Theming;

/// <summary>
/// The native-chrome appearance a theme maps to (drives WPF's Fluent Light/Dark control skin — menus,
/// scrollbars, combo boxes, dialogs — which app-defined hex colors can't reach). Every theme, built-in
/// or custom, picks one of these two; a custom theme is otherwise free to use any colors for the
/// app-specific surfaces it does control (log area, MDI workspace).
/// </summary>
public enum ThemeBaseMode
{
    Light,
    Dark,
}
