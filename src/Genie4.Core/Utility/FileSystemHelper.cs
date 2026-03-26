namespace Genie4.Core.Utility;

public static class FileSystemHelper
{
    public static bool MoveFile(string sourceFileName, string destFileName)
    {
        try
        {
            File.Move(sourceFileName, destFileName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteFile(string sourceFileName)
    {
        try
        {
            File.Delete(sourceFileName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool CreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
