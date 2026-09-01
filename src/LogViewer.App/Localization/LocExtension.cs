using System.Windows.Markup;

namespace LogViewer.App.Localization;

/// <summary>
/// XAML markup extension for localized strings: <c>Header="{loc:Loc Menu_File}"</c>. Resolves once at
/// parse time via <see cref="Loc.Get"/> — the app is restarted to change language, so there is no need
/// for a binding here.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
