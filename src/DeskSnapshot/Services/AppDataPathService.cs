using Windows.Storage;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskSnapshot.Services;

public static class AppDataPathService
{
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
    }

    public static string GetLocalDataFolder()
    {
        if (IsPackaged())
        {
            var packageFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "DeskSnapshot");
            Directory.CreateDirectory(packageFolder);
            return packageFolder;
        }

        var portableFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskSnapshot");
        Directory.CreateDirectory(portableFolder);
        return portableFolder;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
