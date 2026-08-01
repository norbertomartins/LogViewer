namespace LogViewer.Core.Theming;

/// <summary>
/// The app-specific color roles every <see cref="AppTheme"/> defines. Kept as string keys (rather than
/// typed properties) so new roles can be added later without breaking JSON persisted themes.
/// Deliberately small: generic chrome (windows, menus, buttons, scrollbars) is governed by
/// <see cref="AppTheme.BaseMode"/> via WPF's Fluent theme, not by a hex value here — these roles only
/// cover surfaces the app renders itself.
/// </summary>
public static class ThemeColorKeys
{
    public const string BorderColor = "BorderColor";
    public const string WorkspaceBackground = "WorkspaceBackground";
    public const string LogBackground = "LogBackground";
    public const string LogForeground = "LogForeground";
}
