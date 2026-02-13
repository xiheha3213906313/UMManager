using System.Security;

namespace UMManager.WinUI.Helpers;

public static class FileSystemAccessHelper
{
    public static bool CanReadDirectory(string directoryPath)
    {
        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(directoryPath).GetEnumerator();
            enumerator.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    public static bool CanWriteDirectory(string directoryPath)
    {
        try
        {
            var testFilePath = Path.Combine(directoryPath, $".umm_access_test_{Guid.NewGuid():N}.tmp");
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

    public static bool CanReadWriteDirectory(string directoryPath)
        => CanReadDirectory(directoryPath) && CanWriteDirectory(directoryPath);
}

