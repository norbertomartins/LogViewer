namespace LogViewer.App.Tests.TestUtilities;

/// <summary>
/// Tests that briefly create a process-wide WPF <c>Application</c> (offscreen rendering of real views). View-models
/// marshal through <c>Application.Current?.Dispatcher</c>, so a parallel test seeing that instance would post its
/// work to the wrong thread; these run alone (<see cref="CollectionDefinitionAttribute.DisableParallelization"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    public const string Name = "WPF Application";
}
