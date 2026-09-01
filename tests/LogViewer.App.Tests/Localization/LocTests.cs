using System.IO;
using LogViewer.App.Localization;

namespace LogViewer.App.Tests.Localization;

public sealed class LocTests
{
    [Fact]
    public void Get_DefaultCulture_ReturnsEnglish_RegardlessOfOsUiLanguage()
    {
        // Loc.Initialize is never called here, so Culture is null — lookups must still be neutral English.
        Assert.Equal("Cancel", Loc.Get("Common_Cancel"));
        Assert.Equal("Settings", Loc.Get("Settings_Title"));
    }

    [Fact]
    public void PortugueseSatellite_ContainsTranslations()
    {
        // Read the compiled pt-PT satellite directly rather than mutating Loc's global culture.
        var rm = new System.Resources.ResourceManager("LogViewer.App.Localization.Strings", typeof(Loc).Assembly);
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-PT");
        Assert.Equal("Cancelar", rm.GetString("Common_Cancel", pt));
        Assert.Equal("Definições", rm.GetString("Settings_Title", pt));
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKey()
    {
        Assert.Equal("No_Such_Key_123", Loc.Get("No_Such_Key_123"));
    }

    [Fact]
    public void Format_SubstitutesArguments()
    {
        Assert.Equal("Export failed: boom", Loc.Format("Vm_Export_Failed", "boom"));
    }

    [Fact]
    public void EveryNeutralKey_HasPortugueseTranslation()
    {
        var neutral = LoadResxKeys("Strings.resx");
        var pt = LoadResxKeys("Strings.pt-PT.resx");
        var missing = neutral.Except(pt).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Missing pt-PT translations: " + string.Join(", ", missing));
    }

    private static HashSet<string> LoadResxKeys(string fileName)
    {
        // tests run from bin/<cfg>/<tfm>; walk up to the repo and read the source resx directly.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LogViewer.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        var path = Path.Combine(dir!, "src", "LogViewer.App", "Localization", fileName);
        var doc = System.Xml.Linq.XDocument.Load(path);
        return doc.Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
