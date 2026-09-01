using System.Globalization;
using System.Resources;

namespace LogViewer.App.Localization;

/// <summary>
/// Central access point for localized UI strings. Backed by <c>Localization/Strings.resx</c> (neutral,
/// English) plus per-culture satellites (<c>Strings.pt-PT.resx</c> …). The active culture is chosen once
/// at startup from <see cref="LogViewer.Core.Configuration.AppSettings.Language"/> — changing the language
/// requires a restart, so this type is a simple static lookup with no change notification.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources =
        new("LogViewer.App.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>The culture localized lookups resolve against. <c>null</c> means the neutral (English) set.</summary>
    public static CultureInfo? Culture { get; private set; }

    /// <summary>
    /// Applies <paramref name="languageName"/> (a culture name such as <c>en</c> or <c>pt-PT</c>) as the
    /// UI language for this process. Falls back to the neutral resources when the name is blank, "en", or
    /// not a known culture. Call once, before any window is shown.
    /// </summary>
    public static void Initialize(string? languageName)
    {
        // The neutral resources are English (Directory.Build.props NeutralLanguage=en-US), so any "en"
        // culture resolves to them via ResourceManager's own fallback — no satellite needed.
        if (string.IsNullOrWhiteSpace(languageName)
            || languageName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            Culture = null;
            return;
        }

        try
        {
            Culture = CultureInfo.GetCultureInfo(languageName);
            CultureInfo.DefaultThreadCurrentUICulture = Culture;
            Thread.CurrentThread.CurrentUICulture = Culture;
        }
        catch (CultureNotFoundException)
        {
            Culture = null;
        }
    }

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key itself if it is missing. When no
    /// culture has been set (<see cref="Initialize"/> not called, or called with an "en" name), lookups are
    /// pinned to <see cref="CultureInfo.InvariantCulture"/> so the result is the neutral (English) text
    /// regardless of the operating system's UI language.
    /// </summary>
    public static string Get(string key) => Resources.GetString(key, Culture ?? CultureInfo.InvariantCulture) ?? key;

    /// <summary><see cref="Get"/> followed by <see cref="string.Format(IFormatProvider, string, object?[])"/>.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(Culture ?? CultureInfo.InvariantCulture, Get(key), args);
}
