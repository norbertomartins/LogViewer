using System.IO;
using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Configuration;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class NewPatternDetectionTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private static string Baseline(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => i % 10 == 0 ? $"[ERROR] Payment {i} declined\n" : $"[INFO] Request {i} ok\n"));

    [Fact]
    public void FlagsFirstOccurrenceOfANewErrorShape_AndNavigatesToIt()
    {
        var (main, _) = MainViewModelFactory.Create();
        var content = Baseline(300) + "[ERROR] Database connection lost after 30s\n" + "[ERROR] Payment 999 declined\n";
        var doc = main.OpenPath(_tempDir.CreateFile("n.log", content));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 302);

        Assert.Equal(1, doc.NewPatternCount);
        Assert.Equal("🆕 1", doc.NewPatternBadge);
        var flagged = Assert.Single(doc.Lines, l => l.IsNewPattern);
        Assert.Equal(301, flagged.LineNumber);

        doc.SelectedLine = doc.Lines[0];
        doc.NextNewPatternCommand.Execute(null);
        Assert.Equal(301, doc.SelectedLine?.LineNumber);
        main.Dispose();
    }

    [Fact]
    public void NotifiesForNewPatternsArrivingLive_OnlyWhenOptedIn()
    {
        var notifications = Substitute.For<INotificationService>();
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.NotificationAlerts.NotifyOnNewErrorPatterns = true;
        var (main, _) = MainViewModelFactory.Create(settings, notificationService: notifications);

        var path = _tempDir.CreateFile("live.log", Baseline(300));
        var doc = main.OpenPath(path);
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 300);
        notifications.DidNotReceiveWithAnyArgs().Notify(default!, default!);

        File.AppendAllText(path, "[ERROR] Certificate expired for host api.example.com\n");
        TestDispatcher.SpinUntil(() => doc.NewPatternCount >= 1);

        notifications.Received(1).Notify(Arg.Is<string>(t => t.Contains("live.log")), Arg.Is<string>(m => m.Contains("Certificate expired")));
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
