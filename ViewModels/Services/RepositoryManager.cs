namespace KafkaLens.ViewModels.Services;

public class RepositoryManager
{
    private readonly ISettingsService _settings;

    public RepositoryManager(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<string> GetRepositories()
    {
        return _settings.GetPluginSettings().Repositories;
    }

    public void AddRepository(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var ps = _settings.GetPluginSettings();
        if (!ps.Repositories.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            ps.Repositories.Add(url);
            _settings.SavePluginSettings(ps);
        }
    }

    public void RemoveRepository(string url)
    {
        var ps = _settings.GetPluginSettings();
        if (ps.Repositories.Count <= 1) return;
        ps.Repositories.Remove(url);
        _settings.SavePluginSettings(ps);
    }
}
