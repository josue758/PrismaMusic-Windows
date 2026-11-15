using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PrismaMusic;

internal static class NetworkMusicSource
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetGetUniversalNameW(string path, int infoLevel, IntPtr buffer, ref int size);

    internal static string Normalize(string input)
    {
        string path = input.Trim().Trim('"');
        if (path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
            path = @"\\" + uri.Host + Uri.UnescapeDataString(uri.AbsolutePath);
        }

        path = path.Replace('/', Path.DirectorySeparatorChar);
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            path = ResolveMappedDrive(path);
        }
        if (path == null || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2 ? path : null;
    }

    private static string ResolveMappedDrive(string path)
    {
        const int universalNameInfo = 1;
        const int moreData = 234;
        int size = 0;
        if (WNetGetUniversalNameW(path, universalNameInfo, IntPtr.Zero, ref size) != moreData || size <= 0)
            return null;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            return WNetGetUniversalNameW(path, universalNameInfo, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer)) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
