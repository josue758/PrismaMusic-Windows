using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PrismaMusic;

internal static class NetworkMusicSource
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetGetUniversalNameW(string path, int infoLevel, IntPtr buffer, ref int size);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NetShareEnum(string server, int level, out nint buffer, int preferredLength,
        out int entries, out int totalEntries, ref int resume);

    [DllImport("netapi32.dll", ExactSpelling = true)]
    private static extern int NetApiBufferFree(nint buffer);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetAddConnection3W(nint owner, ref NetworkResource resource, string password, string username, int flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo
    {
        internal nint Name;
        internal uint Type;
        internal nint Comment;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetworkResource
    {
        internal int Scope;
        internal int Type;
        internal int DisplayType;
        internal int Usage;
        internal string LocalName;
        internal string RemoteName;
        internal string Comment;
        internal string Provider;
    }

    internal static string ServerOnly(string input)
    {
        string text = CleanInput(input);
        if (text.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            text = text[6..];
        else if (text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal))
            text = text[2..];
        text = text.TrimEnd('/', '\\');
        // A host alone lists shares. A drive letter, path, or URL with credentials is not a host.
        return Uri.CheckHostName(text) is UriHostNameType.Dns or UriHostNameType.IPv4 ? text : null;
    }

    internal static string[] ListShares(string server)
    {
        const int moreData = 234;
        var shares = new List<string>();
        int resume = 0;
        int status;
        do
        {
            int previous = resume;
            status = NetShareEnum(@"\\" + server, 1, out nint buffer, -1, out int entries, out _, ref resume);
            try
            {
                if (status is not (0 or moreData)) throw new Win32Exception(status);
                int size = Marshal.SizeOf<ShareInfo>();
                for (int i = 0; i < entries; i++)
                {
                    var item = Marshal.PtrToStructure<ShareInfo>(buffer + i * size);
                    // List disk shares. Exclude printers, IPC and hidden administrative shares.
                    if ((item.Type & 0xff) == 0 && Marshal.PtrToStringUni(item.Name) is string name && !name.EndsWith('$'))
                        shares.Add($@"\\{server}\{name}");
                }
                if (status == moreData && (entries == 0 || previous == resume))
                    throw new Win32Exception(status);
            }
            finally { if (buffer != 0) NetApiBufferFree(buffer); }
        } while (status == moreData);
        return shares.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static void CheckReadable(string path)
    {
        using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        entries.MoveNext();
    }

    internal static void SignIn(string path, nint owner)
    {
        var parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string share = parts.Length > 1 ? parts[1] : "IPC$";
        var resource = new NetworkResource { Type = parts.Length > 1 ? 1 : 0, RemoteName = $@"\\{parts[0]}\{share}" };
        // Windows owns the password dialog and the SMB session.
        int result = WNetAddConnection3W(owner, ref resource, null, null, 0x00000008 | 0x00000010);
        if (result != 0) throw new Win32Exception(result);
    }

    internal static int ErrorCode(Exception error) => error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xffff;

    internal static bool RequiresSignIn(int errorCode) => errorCode is 5 or 65 or 86 or 1244 or 1326 or 1385 or 1909 or 2202;

    internal static string Normalize(string input)
    {
        string path = CleanInput(input);
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

    private static string CleanInput(string input) => input.Trim().Trim('"');

    private static string ResolveMappedDrive(string path)
    {
        const int universalNameInfo = 1;
        const int moreData = 234;
        int size = 1024;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int result = WNetGetUniversalNameW(path, universalNameInfo, buffer, ref size);
                if (result == 0) return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer));
                if (result != moreData || size <= 0) return null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }
}
