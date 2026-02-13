using System.Security;

namespace UMManager.WinUI.Helpers;

public static class ConfigStorageHelper
{
    private const string AppFolderName = "UMManager";
    private const string ConfigFolderName = "config";

    private static readonly object _lock = new();
    private static string? _configRoot;

    public static string GetConfigRoot()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_configRoot))
                return _configRoot!;

            var portableConfigRoot = Path.Combine(AppContext.BaseDirectory, ConfigFolderName);
            if (TryEnsureWritableDirectory(portableConfigRoot))
                return _configRoot = portableConfigRoot;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fallbackConfigRoot = Path.Combine(localAppData, AppFolderName, ConfigFolderName);
            TryEnsureWritableDirectory(fallbackConfigRoot);
            return _configRoot = fallbackConfigRoot;
        }
    }

    private static bool TryEnsureWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            var testFilePath = Path.Combine(directoryPath, $".umm_config_test_{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(testFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                       FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }

            if (File.Exists(testFilePath))
                File.Delete(testFilePath);

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }
}

