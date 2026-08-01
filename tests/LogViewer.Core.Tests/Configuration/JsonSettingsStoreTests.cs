using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Highlighting;

namespace LogViewer.Core.Tests.Configuration;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(WindowModeKind.Tabbed, settings.DefaultWindowMode);
        Assert.Empty(settings.RecentSources);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSettings()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);
        var original = new AppSettings
        {
            DefaultWindowMode = WindowModeKind.Floating,
            RingBufferCapacity = 12_345,
        };
        original.GlobalHighlightRules.Add(HighlightRule.CreateDefault("Errors", "ERROR"));
        original.RecentSources.Add(new TailSourceSettings { Path = @"C:\logs\app.log" });

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(WindowModeKind.Floating, loaded.DefaultWindowMode);
        Assert.Equal(12_345, loaded.RingBufferCapacity);
        Assert.Single(loaded.GlobalHighlightRules);
        Assert.Equal("ERROR", loaded.GlobalHighlightRules[0].Pattern);
        Assert.Single(loaded.RecentSources);
        Assert.Equal(@"C:\logs\app.log", loaded.RecentSources[0].Path);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        var path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not valid json ");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.NotNull(settings);
        Assert.Equal(WindowModeKind.Tabbed, settings.DefaultWindowMode);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsGeneralizedSourceSettings()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);
        var original = new AppSettings();
        original.RecentSources.Add(new TailSourceSettings
        {
            Kind = TailSourceKind.EventLog,
            EventLogChannelName = "Application",
            EventLogFilters = { EventLogFilterRule.CreateDefault("Errors only", "error") },
            CustomColorHex = "#3366CC",
            CustomIconGlyph = "⭐",
            MdiLeft = 10,
            MdiTop = 20,
            MdiWidth = 480,
            MdiHeight = 320,
            MdiIsMaximized = true,
        });
        original.Layout.ActiveSourceDedupKey = "eventlog:Application";
        original.AutoTuneForRemoteDesktop = false;

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.False(loaded.AutoTuneForRemoteDesktop);
        Assert.Equal("eventlog:Application", loaded.Layout.ActiveSourceDedupKey);

        var entry = Assert.Single(loaded.RecentSources);
        Assert.Equal(TailSourceKind.EventLog, entry.Kind);
        Assert.Equal("Application", entry.EventLogChannelName);
        Assert.Single(entry.EventLogFilters);
        Assert.Equal("#3366CC", entry.CustomColorHex);
        Assert.Equal("⭐", entry.CustomIconGlyph);
        Assert.Equal(10, entry.MdiLeft);
        Assert.Equal(20, entry.MdiTop);
        Assert.Equal(480, entry.MdiWidth);
        Assert.Equal(320, entry.MdiHeight);
        Assert.True(entry.MdiIsMaximized);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "LogViewerTests_" + Guid.NewGuid().ToString("N"), "settings.json");
}
