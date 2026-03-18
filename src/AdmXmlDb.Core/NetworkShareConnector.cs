using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AdmXmlDb.Core;

/// <summary>
/// Establishes authenticated connections to UNC network shares using Win32 WNetAddConnection2.
/// Call <see cref="Connect"/> before accessing a UNC path that requires credentials,
/// and <see cref="Dispose"/> when done.
/// </summary>
public sealed class NetworkShareConnector : IDisposable
{
    private readonly string _remotePath;
    private bool _connected;

    private NetworkShareConnector(string remotePath)
    {
        _remotePath = remotePath;
    }

    /// <summary>
    /// Extracts the \\server\share root from a full UNC path.
    /// </summary>
    public static string? GetShareRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(@"\\"))
            return null;

        var parts = path.TrimStart('\\').Split('\\');
        if (parts.Length < 2)
            return null;

        return $@"\\{parts[0]}\{parts[1]}";
    }

    /// <summary>
    /// Returns true if the path is a UNC network path.
    /// </summary>
    public static bool IsUncPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.StartsWith(@"\\");

    /// <summary>
    /// Connects to a UNC share with the given credentials. Returns null if not a UNC path
    /// or credentials are empty. Throws Win32Exception on failure.
    /// </summary>
    public static NetworkShareConnector? Connect(string path, string? username, string? password)
    {
        if (!IsUncPath(path) || string.IsNullOrWhiteSpace(username))
            return null;

        var shareRoot = GetShareRoot(path);
        if (shareRoot == null)
            return null;

        var connector = new NetworkShareConnector(shareRoot);

        var netResource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = shareRoot
        };

        var result = WNetAddConnection2(ref netResource, password ?? "", username, CONNECT_TEMPORARY);

        // ERROR_SESSION_CREDENTIAL_CONFLICT (1219) means already connected - that's fine
        if (result != 0 && result != 1219)
            throw new Win32Exception(result, $"WNetAddConnection2 failed for {shareRoot}: error {result}");

        connector._connected = result == 0;
        return connector;
    }

    public void Dispose()
    {
        if (_connected)
        {
            WNetCancelConnection2(_remotePath, 0, true);
            _connected = false;
        }
    }

    private const int RESOURCETYPE_DISK = 1;
    private const int CONNECT_TEMPORARY = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);
}
