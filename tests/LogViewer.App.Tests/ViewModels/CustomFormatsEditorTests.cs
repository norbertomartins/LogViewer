using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

[Collection(ParserRegistryCollection.Name)]
public sealed class CustomFormatsEditorTests
{
    [Fact]
    public void CustomFormatsEditor_PreviewsSampleLines_AndValidates()
    {
        var format = new CustomLogFormat { Name = "Mine", Pattern = @"^(?<level>INFO|ERROR) (?<message>.*)$" };
        var vm = new CustomFormatsEditorViewModel([format], ["INFO hello", "garbage", "ERROR boom"]);

        Assert.Same(vm.Formats[0], vm.SelectedFormat);
        Assert.Equal(3, vm.SampleResults.Count);
        Assert.Equal([true, false, true], vm.SampleResults.Select(r => r.Matched));
        Assert.Equal("Error", vm.SampleResults[2].Level);
        Assert.True(vm.CanSave);

        vm.SelectedFormat!.Pattern = "(broken";
        Assert.NotNull(vm.SelectedFormat.Error);
        Assert.False(vm.CanSave);

        vm.AddFromExampleCommand.Execute(CustomLogFormatExamples.All[0]);
        Assert.Equal(2, vm.Formats.Count);
        Assert.NotEqual(CustomLogFormatExamples.All[0].Id, vm.Formats[1].Id);
    }

    [Fact]
    public void MainViewModel_EditCustomFormats_PersistsAndRegisters()
    {
        var dialogs = Substitute.For<IDialogService>();
        var edited = new CustomLogFormat { Name = "Edited", Pattern = @"^@@APPTEST@@ (?<message>.*)$" };
        dialogs.ShowCustomFormatsEditor(Arg.Any<IReadOnlyList<CustomLogFormat>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns([edited]);
        var (main, settings) = MainViewModelFactory.Create(dialogService: dialogs);
        try
        {
            main.EditCustomFormatsCommand.Execute(null);

            Assert.Equal(edited.Id, Assert.Single(settings.CustomLogFormats).Id);
            Assert.Contains(edited.Id, LogLineParsers.FormatIds);
        }
        finally
        {
            LogLineParsers.SetCustomFormats([]);
            main.Dispose();
        }
    }
}
