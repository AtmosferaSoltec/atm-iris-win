using System;
using System.IO;
using Windows.Storage;

namespace Iris.Shell.Platform;

/// <summary>
/// Local folders. Packaged: the app's LocalState/LocalCache. Unpackaged (dev copy): <c>%LOCALAPPDATA%\Iris</c>,
/// because <see cref="ApplicationData.Current"/> needs package identity.
/// </summary>
public static class AppPaths
{
    static AppPaths()
    {
        try
        {
            LocalFolder = ApplicationData.Current.LocalFolder.Path;
            CacheFolder = ApplicationData.Current.LocalCacheFolder.Path;
            IsPackaged = true;
        }
        catch (Exception)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Iris");
            LocalFolder = root;
            CacheFolder = Path.Combine(root, "cache");
        }

        Directory.CreateDirectory(LocalFolder);
        Directory.CreateDirectory(CacheFolder);
    }

    public static bool IsPackaged { get; }

    public static string LocalFolder { get; }

    public static string CacheFolder { get; }

    public static string LogsFolder => Path.Combine(LocalFolder, "logs");

    public static string File(string name) => Path.Combine(LocalFolder, name);
}
