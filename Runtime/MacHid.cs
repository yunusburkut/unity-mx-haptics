#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MxHaptics
{
    static class HidTransport
    {
        const int KIOReturnNotPermitted = unchecked((int)0xE00002E2);
        const string PermissionHint = "macOS blocked access to the mouse. Allow Unity (or your game) in System Settings > Privacy & Security > Input Monitoring, then restart it.";

        // Same setup as hidapi: one manager that only lists devices, scheduled on the worker thread's run loop,
        // which is pumped before each search so plugged / unplugged devices show up. The manager is never opened:
        // IOHIDManagerOpen would open every keyboard too and ask for Input Monitoring even without the mouse.
        // Only Logitech candidates are opened, one by one, below. One manager per domain load is left behind on
        // Editor domain reloads; harmless, and releasing it could release devices still in use.
        static IntPtr manager;

        // Logitech HID devices that accept 20-byte output reports; the HID++ resolve step rejects the rest.
        public static IEnumerable<IHidTransport> OpenCandidates()
        {
            if (manager == IntPtr.Zero)
            {
                manager = Mac.IOHIDManagerCreate(IntPtr.Zero, 0);
                Mac.IOHIDManagerSetDeviceMatching(manager, IntPtr.Zero);
                Mac.IOHIDManagerScheduleWithRunLoop(manager, Mac.CFRunLoopGetCurrent(), Mac.DefaultMode);
            }
            Mac.CFRunLoopRunInMode(Mac.DefaultMode, 0.001, false);   // let the manager process device arrivals
            IntPtr set = Mac.IOHIDManagerCopyDevices(manager);
            if (set == IntPtr.Zero) yield break;
            try
            {
                var devices = new IntPtr[(int)Mac.CFSetGetCount(set)];
                Mac.CFSetGetValues(set, devices);
                foreach (IntPtr device in devices)
                {
                    if (Mac.GetIntProperty(device, "VendorID") != HidppHapticDevice.LogitechVid) continue;
                    if (Mac.GetIntProperty(device, "MaxOutputReportSize") < HidppHapticDevice.LongLength) continue;
                    int status = Mac.IOHIDDeviceOpen(device, 0);
                    if (status == KIOReturnNotPermitted) MxHapticsClient.WarnOnce(PermissionHint);
                    if (status != 0) continue;
                    yield return new MacHid(device);
                }
            }
            finally
            {
                Mac.CFRelease(set);
            }
        }
    }

    // IOHIDDeviceSetReport for writes; input reports arrive through a callback on the worker thread's run loop,
    // which Read pumps.
    sealed class MacHid : IHidTransport
    {
        const int InputBufferLength = 64;
        static readonly Mac.ReportCallback OnReport = HandleReport;   // kept alive: native code holds the pointer

        readonly IntPtr device;
        readonly IntPtr runLoop;
        readonly IntPtr inputBuffer = Marshal.AllocHGlobal(InputBufferLength);
        readonly IntPtr outputBuffer = Marshal.AllocHGlobal(HidppHapticDevice.LongLength);
        readonly GCHandle self;
        readonly Queue<byte[]> received = new Queue<byte[]>();

        public MacHid(IntPtr device)
        {
            this.device = Mac.CFRetain(device);
            self = GCHandle.Alloc(this);
            runLoop = Mac.CFRunLoopGetCurrent();
            Mac.IOHIDDeviceRegisterInputReportCallback(device, inputBuffer, (IntPtr)InputBufferLength, OnReport, GCHandle.ToIntPtr(self));
            Mac.IOHIDDeviceScheduleWithRunLoop(device, runLoop, Mac.DefaultMode);
        }

        public bool Write(byte[] report)
        {
            Marshal.Copy(report, 0, outputBuffer, HidppHapticDevice.LongLength);
            // Numbered report: the buffer starts with the report ID, as hidapi does.
            return Mac.IOHIDDeviceSetReport(device, 1 /* output */, (IntPtr)report[0], outputBuffer, (IntPtr)HidppHapticDevice.LongLength) == 0;
        }

        public int Read(byte[] destination, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (received.Count == 0)
            {
                int left = deadline - Environment.TickCount;
                if (left <= 0) return 0;
                // 1 = kCFRunLoopRunFinished: the run loop has no sources, so it returns at once; stop instead of spinning.
                if (Mac.CFRunLoopRunInMode(Mac.DefaultMode, left / 1000.0, true) == 1) return 0;
            }
            byte[] r = received.Dequeue();
            int n = Math.Min(r.Length, destination.Length);
            Array.Copy(r, destination, n);
            return n;
        }

        [AOT.MonoPInvokeCallback(typeof(Mac.ReportCallback))]
        static void HandleReport(IntPtr context, int result, IntPtr sender, int type, uint reportId, IntPtr report, IntPtr length)
        {
            var hid = GCHandle.FromIntPtr(context).Target as MacHid;
            int n = (int)length;
            if (hid == null || result != 0 || n <= 0 || hid.received.Count >= 32) return;
            // Make sure the copy starts with the report ID, whether or not IOKit included it.
            bool hasId = Marshal.ReadByte(report) == (byte)reportId;
            var copy = new byte[hasId ? n : n + 1];
            Marshal.Copy(report, copy, hasId ? 0 : 1, n);
            if (!hasId) copy[0] = (byte)reportId;
            hid.received.Enqueue(copy);
        }

        public void Dispose()
        {
            Mac.IOHIDDeviceRegisterInputReportCallback(device, inputBuffer, (IntPtr)InputBufferLength, null, IntPtr.Zero);
            Mac.IOHIDDeviceUnscheduleFromRunLoop(device, runLoop, Mac.DefaultMode);
            Mac.IOHIDDeviceClose(device, 0);
            Mac.CFRelease(device);
            self.Free();
            Marshal.FreeHGlobal(inputBuffer);
            Marshal.FreeHGlobal(outputBuffer);
        }
    }

    static class Mac
    {
        const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
        const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const uint Utf8 = 0x08000100;
        const int SInt32Type = 3;

        // Run loop modes are compared by value, so a CFString with the same text works as kCFRunLoopDefaultMode.
        public static readonly IntPtr DefaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", Utf8);

        public static int GetIntProperty(IntPtr device, string key)
        {
            IntPtr cfKey = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
            try
            {
                IntPtr value = IOHIDDeviceGetProperty(device, cfKey);   // not owned: no release
                return value != IntPtr.Zero && CFNumberGetValue(value, SInt32Type, out int result) ? result : 0;
            }
            finally { CFRelease(cfKey); }
        }

        [DllImport(IOKit)] public static extern IntPtr IOHIDManagerCreate(IntPtr allocator, int options);
        [DllImport(IOKit)] public static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);
        [DllImport(IOKit)] public static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);
        [DllImport(IOKit)] public static extern void IOHIDManagerScheduleWithRunLoop(IntPtr manager, IntPtr runLoop, IntPtr mode);
        [DllImport(IOKit)] public static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);
        [DllImport(IOKit)] public static extern int IOHIDDeviceOpen(IntPtr device, int options);
        [DllImport(IOKit)] public static extern int IOHIDDeviceClose(IntPtr device, int options);
        [DllImport(IOKit)] public static extern int IOHIDDeviceSetReport(IntPtr device, int type, IntPtr reportId, IntPtr report, IntPtr length);
        [DllImport(IOKit)] public static extern void IOHIDDeviceRegisterInputReportCallback(IntPtr device, IntPtr report, IntPtr length, ReportCallback callback, IntPtr context);
        [DllImport(IOKit)] public static extern void IOHIDDeviceScheduleWithRunLoop(IntPtr device, IntPtr runLoop, IntPtr mode);
        [DllImport(IOKit)] public static extern void IOHIDDeviceUnscheduleFromRunLoop(IntPtr device, IntPtr runLoop, IntPtr mode);

        [DllImport(CF)] public static extern IntPtr CFRetain(IntPtr cf);
        [DllImport(CF)] public static extern void CFRelease(IntPtr cf);
        [DllImport(CF)] public static extern IntPtr CFSetGetCount(IntPtr set);
        [DllImport(CF)] public static extern void CFSetGetValues(IntPtr set, [Out] IntPtr[] values);
        // CoreFoundation's Boolean is 1 byte: marshal as I1, not the 4-byte default.
        [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
        [DllImport(CF)] public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
        [DllImport(CF)] public static extern IntPtr CFRunLoopGetCurrent();
        [DllImport(CF)] public static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);

        public delegate void ReportCallback(IntPtr context, int result, IntPtr sender, int type, uint reportId, IntPtr report, IntPtr length);
    }
}
#endif
