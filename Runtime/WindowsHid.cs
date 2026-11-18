#if !MXHAPTICS_MAC && (UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN) || MXHAPTICS_WINDOWS)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MxHaptics
{
    static class HidTransport
    {
        // Logitech vendor collections (page 0xFF00 receiver / 0xFF43 Bluetooth) with 20-byte output reports.
        public static IEnumerable<IHidTransport> OpenCandidates()
        {
            foreach (string path in Native.EnumerateHidPaths())
            {
                var h = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite,
                    Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, Native.FileFlagOverlapped, IntPtr.Zero);
                if (h.IsInvalid) { h.Dispose(); continue; }   // mouse/keyboard collections are opened exclusively by Windows

                int inputLength;
                try
                {
                    if (!IsHidppLongCollection(h, out inputLength)) { h.Dispose(); continue; }
                }
                catch
                {
                    h.Dispose();
                    throw;
                }
                yield return new WindowsHid(h, inputLength);
            }
        }

        static bool IsHidppLongCollection(SafeFileHandle h, out int inputLength)
        {
            inputLength = 0;
            var attributes = new Native.HiddAttributes { Size = Marshal.SizeOf(typeof(Native.HiddAttributes)) };
            if (!Native.HidD_GetAttributes(h, ref attributes) || attributes.VendorID != HidppHapticDevice.LogitechVid) return false;
            if (!Native.HidD_GetPreparsedData(h, out IntPtr preparsed)) return false;
            try
            {
                if (Native.HidP_GetCaps(preparsed, out Native.HidpCaps caps) != Native.HidpStatusSuccess) return false;
                inputLength = caps.InputReportByteLength;
                return caps.UsagePage >= 0xFF00 && caps.OutputReportByteLength == HidppHapticDevice.LongLength;
            }
            finally
            {
                Native.HidD_FreePreparsedData(preparsed);
            }
        }
    }

    // Overlapped ReadFile/WriteFile on one HID collection.
    sealed class WindowsHid : IHidTransport
    {
        readonly SafeFileHandle handle;
        readonly int bufferLength;
        readonly IntPtr buffer;
        readonly IntPtr overlapped;
        readonly ManualResetEvent ioDone = new ManualResetEvent(false);

        public WindowsHid(SafeFileHandle handle, int inputLength)
        {
            this.handle = handle;
            bufferLength = Math.Max(inputLength, HidppHapticDevice.LongLength);
            buffer = Marshal.AllocHGlobal(bufferLength);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
        }

        public bool Write(byte[] report)
        {
            Marshal.Copy(report, 0, buffer, HidppHapticDevice.LongLength);
            return Transfer(write: true, length: HidppHapticDevice.LongLength, timeoutMs: 300) > 0;
        }

        public int Read(byte[] destination, int timeoutMs)
        {
            int n = Transfer(write: false, length: bufferLength, timeoutMs: timeoutMs);
            n = Math.Min(n, destination.Length);
            if (n > 0) Marshal.Copy(buffer, destination, 0, n);
            return n;
        }

        // Always waits for the I/O to finish (or be cancelled) before returning, so buffer and overlapped
        // are never freed or reused while Windows still owns them.
        int Transfer(bool write, int length, int timeoutMs)
        {
            ioDone.Reset();
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ioDone.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            bool started = write
                ? Native.WriteFile(handle, buffer, length, IntPtr.Zero, overlapped)
                : Native.ReadFile(handle, buffer, length, IntPtr.Zero, overlapped);
            if (!started && Marshal.GetLastWin32Error() != Native.ErrorIoPending) return 0;
            if (!ioDone.WaitOne(timeoutMs)) Native.CancelIoEx(handle, overlapped);
            return Native.GetOverlappedResult(handle, overlapped, out int bytes, true) ? bytes : 0;
        }

        public void Dispose()
        {
            handle.Dispose();   // Transfer never leaves I/O in flight
            ioDone.Close();
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(overlapped);
        }
    }

    static class Native
    {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        public const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3, FileFlagOverlapped = 0x40000000;
        public const int ErrorIoPending = 997;
        public const int HidpStatusSuccess = 0x00110000;
        const int DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct HiddAttributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidpCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Rest;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SpDeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        public static List<string> EnumerateHidPaths()
        {
            var paths = new List<string>();
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == new IntPtr(-1)) return paths;
            try
            {
                var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData)) };
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(detail + 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return paths;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int length, IntPtr read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int length, IntPtr written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int bytes, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HiddAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SpDeviceInterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
#endif
