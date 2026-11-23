using System.IO;

namespace SlotCast.Services;

public static class SlotCastPaths
{
    // Never include an app version or installation directory in the user-data path.
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlotCast");
    public static string DatabaseFile => Path.Combine(DataDirectory, "slotcast.db");
}
