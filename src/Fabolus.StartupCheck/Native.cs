using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Fabolus.StartupCheck {

    internal static class Native {
        public const uint GenericRead = 0x80000000;
        public const uint ShareAll = 0x1 | 0x2 | 0x4;
        public const uint OpenExisting = 3;
        public const uint FlagNoBuffering = 0x20000000;
        public const uint FlagSequentialScan = 0x08000000;
        public const uint FlagBackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(
            string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint toRead, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);

        public const uint MemCommitReserve = 0x1000 | 0x2000;
        public const uint MemRelease = 0x8000;
        public const uint PageReadWrite = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryStatusEx {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        public static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

        public static string? KnownFolderPath(Guid id) {
            var guid = id;
            if (SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out var path) != 0) return null;
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        public const int SmRemoteSession = 0x1000;

        // --- WinVerifyTrust -------------------------------------------------------------------

        public static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        public struct WinTrustFileInfo {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WinTrustData {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProvFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", SetLastError = false)]
        public static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

        public const int TrustNoSignature = unchecked((int)0x800B0100);

        /// <summary>
        /// Checks a file's embedded Authenticode signature without UI or revocation lookups, so it
        /// does not wait on the network. Returns 0 for a valid signature, an HRESULT otherwise.
        /// </summary>
        public static int VerifySignature(string path) {
            var pathPtr = Marshal.StringToHGlobalUni(path);
            var fileInfo = new WinTrustFileInfo { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPtr };
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try {
                Marshal.StructureToPtr(fileInfo, filePtr, false);
                var data = new WinTrustData {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                    UiChoice = 2,          // WTD_UI_NONE
                    RevocationChecks = 0,  // WTD_REVOKE_NONE
                    UnionChoice = 1,       // WTD_CHOICE_FILE
                    FileInfo = filePtr,
                    StateAction = 1,       // WTD_STATEACTION_VERIFY
                    ProvFlags = 0x10 | 0x1000, // WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                var action = GenericVerifyV2;
                var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);

                data.StateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                return result;
            }
            finally {
                Marshal.FreeHGlobal(filePtr);
                Marshal.FreeHGlobal(pathPtr);
            }
        }
    }
}
