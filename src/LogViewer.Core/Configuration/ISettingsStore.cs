namespace LogViewer.Core.Configuration;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
