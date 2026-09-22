// Every test here launches a real LogViewer.App.exe and drives it via IsolatedSettingsFixture, which
// moves the *actual* %LOCALAPPDATA%\LogViewer\settings.json aside and restores it afterwards. xUnit
// parallelizes across test classes by default, so without this, tests from different classes race on
// that one shared file — one test's fixture can swap the settings file out from under another test's
// still-running app instance, producing flaky, hard-to-diagnose failures (wrong document restored,
// toolbar buttons missing, etc.) that have nothing to do with the feature under test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
