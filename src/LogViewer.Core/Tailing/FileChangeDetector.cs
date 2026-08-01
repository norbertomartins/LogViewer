using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LogViewer.Core.Tailing;

public enum FileChangeKind
{
    /// <summary>First observation of the file, or a normal append since the last check — no reset needed.</summary>
    None,

    /// <summary>The file was truncated in place (its identity is unchanged but its length shrank).</summary>
    Truncated,

    /// <summary>The path now resolves to a different underlying file (rename-and-recreate rotation).</summary>
    Rotated,

    /// <summary>The file does not currently exist at the path.</summary>
    Deleted,
}

/// <summary>
/// Detects truncation, rename-and-recreate rotation, and deletion of a tailed file across successive
/// checks, using the NTFS file identity (volume serial + file index) rather than the path alone so
/// "delete old file, create new file with the same name" rotation is distinguished from in-place growth.
/// </summary>
public sealed class FileChangeDetector
{
    private FileIdentity? _lastIdentity;
    private long _lastKnownLength;

    /// <summary>Length observed on the most recent successful check, valid after a <see cref="FileChangeKind.None"/> result.</summary>
    public long LastKnownLength => _lastKnownLength;

    public FileChangeKind Check(string path)
    {
        if (!File.Exists(path))
        {
            _lastIdentity = null;
            return FileChangeKind.Deleted;
        }

        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var identity = FileIdentity.FromHandle(handle);
        var length = RandomAccess.GetLength(handle);

        if (_lastIdentity is null)
        {
            _lastIdentity = identity;
            _lastKnownLength = length;
            return FileChangeKind.None;
        }

        if (!_lastIdentity.Value.Equals(identity))
        {
            _lastIdentity = identity;
            _lastKnownLength = length;
            return FileChangeKind.Rotated;
        }

        if (length < _lastKnownLength)
        {
            _lastKnownLength = length;
            return FileChangeKind.Truncated;
        }

        _lastKnownLength = length;
        return FileChangeKind.None;
    }

    /// <summary>Forgets any previously observed identity, so the next <see cref="Check"/> is treated as a first observation.</summary>
    public void Reset()
    {
        _lastIdentity = null;
        _lastKnownLength = 0;
    }

    private readonly record struct FileIdentity(uint VolumeSerialNumber, uint FileIndexHigh, uint FileIndexLow)
    {
        public static FileIdentity FromHandle(SafeFileHandle handle)
        {
            if (!OperatingSystem.IsWindows())
            {
                // Non-Windows fallback: no stable file-id API without further P/Invoke, so identity
                // tracking degrades to "assume same file" and truncation is still caught via length.
                return new FileIdentity(0, 0, 0);
            }

            if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
            {
                throw new IOException($"GetFileInformationByHandle failed with error {Marshal.GetLastWin32Error()}.");
            }

            return new FileIdentity(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

        [StructLayout(LayoutKind.Sequential)]
        public struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public FileTime CreationTime;
            public FileTime LastAccessTime;
            public FileTime LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FileTime
        {
            public uint DwLowDateTime;
            public uint DwHighDateTime;
        }
    }
}
