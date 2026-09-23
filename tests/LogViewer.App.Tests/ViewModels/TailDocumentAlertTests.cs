using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Configuration;
using LogViewer.Core.Highlighting;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentAlertTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void ErrorLines_ReachingThreshold_RaisesOneNotification()
    {
        var rule = HighlightRule.CreateDefault("Errors", "ERROR") with
        {
            AlertEnabled = true,
            AlertThresholdCount = 2,
            AlertWindowSeconds = 60,
        };
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.HighlightPresets.Add(new HighlightPreset { Rules = [rule] });

        var notificationService = Substitute.For<INotificationService>();
        var (viewModel, _) = MainViewModelFactory.Create(settings, notificationService: notificationService);

        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", "ERROR one\nERROR two\n"));
        SpinUntil(() => doc.Lines.Count >= 2);

        notificationService.Received(1).Notify(Arg.Any<string>(), Arg.Any<string>());
        var alert = Assert.Single(doc.Alerts.Snapshot());
        Assert.Equal(AlertKind.Threshold, alert.Kind);
        Assert.Equal("Errors", alert.RuleName);
        Assert.Equal(2, alert.LineNumber);
        Assert.Equal("ERROR two", alert.LineText);

        viewModel.Dispose();
    }

    [Fact]
    public void ErrorLines_BelowThreshold_NeverNotifies()
    {
        var rule = HighlightRule.CreateDefault("Errors", "ERROR") with
        {
            AlertEnabled = true,
            AlertThresholdCount = 5,
            AlertWindowSeconds = 60,
        };
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.HighlightPresets.Add(new HighlightPreset { Rules = [rule] });

        var notificationService = Substitute.For<INotificationService>();
        var (viewModel, _) = MainViewModelFactory.Create(settings, notificationService: notificationService);

        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", "ERROR one\nERROR two\n"));
        SpinUntil(() => doc.Lines.Count >= 2);

        notificationService.DidNotReceive().Notify(Arg.Any<string>(), Arg.Any<string>());

        viewModel.Dispose();
    }

    [Fact]
    public void AlertDisabledGlobally_NeverNotifies_ButStillRecordsTheAlertForMcp()
    {
        var rule = HighlightRule.CreateDefault("Errors", "ERROR") with
        {
            AlertEnabled = true,
            AlertThresholdCount = 1,
            AlertWindowSeconds = 60,
        };
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.HighlightPresets.Add(new HighlightPreset { Rules = [rule] });
        settings.NotificationAlerts.Enabled = false;

        var notificationService = Substitute.For<INotificationService>();
        var (viewModel, _) = MainViewModelFactory.Create(settings, notificationService: notificationService);

        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", "ERROR one\n"));
        SpinUntil(() => doc.Lines.Count >= 1);

        notificationService.DidNotReceive().Notify(Arg.Any<string>(), Arg.Any<string>());
        Assert.Single(doc.Alerts.Snapshot());

        viewModel.Dispose();
    }

    private static void SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }

    public void Dispose() => _tempDir.Dispose();
}
