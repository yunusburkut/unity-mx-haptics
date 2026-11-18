#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || MXHAPTICS_WINDOWS
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MxHaptics
{
    // One Logitech HID++ 2.0 device that exposes feature 0x19b0 (HapticFeedback), opened through the
    // Windows HID class driver. Used only from the MxHaptics worker thread.
    sealed class HidppHapticDevice : IDisposable
    {
        const ushort LogitechVid = 0x046D;
        const ushort HapticFeature = 0x19B0;
        const byte LongReport = 0x11;      // HID++ long report: 20 bytes
        const int LongLength = 20;
        const byte SoftwareId = 0x0B;      // low nibble of byte 3; tells our replies apart from other apps'
        const int ReplyTimeoutMs = 300;

        // 0xFF = the device itself (Bluetooth); 1..6 = a slot on a Bolt / Unifying receiver.
        static readonly byte[] DeviceIndices = { 0xFF, 1, 2, 3, 4, 5, 6 };

        readonly SafeFileHandle handle;
        readonly int inputLength;
        readonly IntPtr buffer;
        readonly IntPtr overlapped;
        readonly ManualResetEvent ioDone = new ManualResetEvent(false);
        byte deviceIndex, featureIndex;

        HidppHapticDevice(SafeFileHandle handle, int inputLength)
        {
            this.handle = handle;
            this.inputLength = Math.Max(inputLength, LongLength);
            buffer = Marshal.AllocHGlobal(this.inputLength);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
        }

        // Enumerates HID interfaces and returns the first Logitech HID++ one with a haptic engine, or null.
        public static HidppHapticDevice Find()
        {
            foreach (string path in Native.EnumerateHidPaths())
            {
                var h = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite,
                    Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, Native.FileFlagOverlapped, IntPtr.Zero);
                if (h.IsInvalid) { h.Dispose(); continue; }   // mouse/keyboard collections are opened exclusively by Windows

                HidppHapticDevice device = null;
                try
                {
                    if (!IsHidppLongCollection(h, out int inputLength)) { h.Dispose(); continue; }
                    device = new HidppHapticDevice(h, inputLength);
                    if (device.Resolve()) return device;
                    device.Dispose();
                }
                catch
                {
                    if (device != null) device.Dispose(); else h.Dispose();   // the device owns h once created
                    throw;
                }
            }
            return null;
        }

        static bool IsHidppLongCollection(SafeFileHandle h, out int inputLength)
        {
            inputLength = 0;
            var attributes = new Native.HiddAttributes { Size = Marshal.SizeOf(typeof(Native.HiddAttributes)) };
            if (!Native.HidD_GetAttributes(h, ref attributes) || attributes.VendorID != LogitechVid) return false;
            if (!Native.HidD_GetPreparsedData(h, out IntPtr preparsed)) return false;
            try
            {
                if (Native.HidP_GetCaps(preparsed, out Native.HidpCaps caps) != Native.HidpStatusSuccess) return false;
                inputLength = caps.InputReportByteLength;
                // Vendor page 0xFF00 (receiver) or 0xFF43 (Bluetooth) collection that carries 20-byte reports.
                return caps.UsagePage >= 0xFF00 && caps.OutputReportByteLength == LongLength;
            }
            finally
            {
                Native.HidD_FreePreparsedData(preparsed);
            }
        }

        // IRoot (feature index 0) function 0 = GetFeature(featureId) -> index of that feature on the device.
        bool Resolve()
        {
            foreach (byte index in DeviceIndices)
            {
                Fill(index, 0x00, 0x00, (byte)(HapticFeature >> 8), (byte)(HapticFeature & 0xFF));
                if (!Transfer(write: true, timeoutMs: ReplyTimeoutMs)) continue;

                int deadline = Environment.TickCount + ReplyTimeoutMs;
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    if (left <= 0 || !Transfer(write: false, timeoutMs: left)) break;
                    if (Marshal.ReadByte(buffer, 1) != index) continue;
                    byte feature = Marshal.ReadByte(buffer, 2);
                    // HID++ 1.0 (0x8F) / 2.0 (0xFF) error: bytes 3-4 echo our request (root index 0, function 0 | SoftwareId).
                    // Errors that echo another app's request are not ours.
                    if (feature == 0x8F || feature == 0xFF)
                    {
                        if (Marshal.ReadByte(buffer, 3) == 0x00 && Marshal.ReadByte(buffer, 4) == SoftwareId) break;
                        continue;
                    }
                    if (Marshal.ReadByte(buffer, 0) != LongReport || feature != 0x00
                        || Marshal.ReadByte(buffer, 3) != SoftwareId) continue;   // a notification or another app's reply
                    byte found = Marshal.ReadByte(buffer, 4);
                    if (found == 0) break;   // root answered: feature not supported
                    deviceIndex = index;
                    featureIndex = found;
                    return true;
                }
            }
            return false;
        }

        // HapticFeedback function 4 = play(waveformId). The reply is not awaited.
        public bool Play(byte waveform)
        {
            Fill(deviceIndex, featureIndex, 4, waveform, 0);
            return Transfer(write: true, timeoutMs: ReplyTimeoutMs);
        }

        void Fill(byte device, byte feature, byte function, byte p0, byte p1)
        {
            for (int i = 0; i < LongLength; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteByte(buffer, 0, LongReport);
            Marshal.WriteByte(buffer, 1, device);
            Marshal.WriteByte(buffer, 2, feature);
            Marshal.WriteByte(buffer, 3, (byte)((function << 4) | SoftwareId));
            Marshal.WriteByte(buffer, 4, p0);
            Marshal.WriteByte(buffer, 5, p1);
        }

        // Overlapped ReadFile/WriteFile with a timeout. Always waits for the I/O to finish (or be cancelled)
        // before returning, so buffer and overlapped are never freed or reused while Windows still owns them.
        bool Transfer(bool write, int timeoutMs)
        {
            ioDone.Reset();
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ioDone.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            bool started = write
                ? Native.WriteFile(handle, buffer, LongLength, IntPtr.Zero, overlapped)
                : Native.ReadFile(handle, buffer, inputLength, IntPtr.Zero, overlapped);
            if (!started && Marshal.GetLastWin32Error() != Native.ErrorIoPending) return false;
            if (!ioDone.WaitOne(timeoutMs)) Native.CancelIoEx(handle, overlapped);
            return Native.GetOverlappedResult(handle, overlapped, out int bytes, true) && bytes > 0;
        }

        public void Dispose()
        {
            handle.Dispose();   // closing the handle cancels nothing pending: Transfer never leaves I/O in flight
            ioDone.Close();
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(overlapped);
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

            public static System.Collections.Generic.List<string> EnumerateHidPaths()
            {
                var paths = new System.Collections.Generic.List<string>();
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
}
#endif
