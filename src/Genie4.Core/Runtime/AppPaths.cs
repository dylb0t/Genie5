using System.Runtime.InteropServices;

namespace Genie4.Core.Runtime;

public sealed class AppPaths
{
    public AppPaths(string basePath, bool isLocal)
    {
        BasePath = Path.GetFullPath(basePath);
        IsLocal = isLocal;
    }

    public string BasePath { get; }
    public bool IsLocal { get; }

    public static AppPaths Discover(string appName, string baseDirectory)
    {
        var localConfig = Path.Combine(baseDirectory, "Config");
        if (Directory.Exists(localConfig))
        {
            return new AppPaths(baseDirectory, isLocal: true);
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userDir = Path.Combine(roaming, appName);
        Directory.CreateDirectory(userDir);
        return new AppPaths(userDir, isLocal: false);
    }

    public string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return BasePath;
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(BasePath, configuredPath));
    }

    public string ValidateDirectory(string configuredPath)
    {
        var fullPath = ResolvePath(configuredPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
