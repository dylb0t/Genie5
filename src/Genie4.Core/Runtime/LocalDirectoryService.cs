namespace Genie4.Core.Runtime;

public sealed class LocalDirectoryService
{
    private readonly string _appName;
    private readonly string _baseDirectory;

    public LocalDirectoryService(string appName, string baseDirectory)
    {
        _appName = appName;
        _baseDirectory = baseDirectory;
        Current = AppPaths.Discover(appName, baseDirectory);
    }

    public AppPaths Current { get; private set; }

    public void CheckUserDirectory()
    {
        Current = AppPaths.Discover(_appName, _baseDirectory);
    }

    public void SetUserDataDirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userDir = Path.Combine(roaming, _appName);
        Directory.CreateDirectory(userDir);
        Current = new AppPaths(userDir, isLocal: false);
    }

    public string ValidateDirectory(string configuredPath) => Current.ValidateDirectory(configuredPath);
}
