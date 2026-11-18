#if !UNITY_EDITOR_LINUX   // the asmdef already limits the package to the Editor and Windows / macOS players
using System;

namespace MxHaptics
{
    // One opened HID interface. WindowsHid.cs and MacHid.cs implement it; HidTransport.OpenCandidates()
    // lists the Logitech interfaces that can carry 20-byte HID++ reports.
    interface IHidTransport : IDisposable
    {
        bool Write(byte[] report);                       // report[0] is the report ID
        int Read(byte[] buffer, int timeoutMs);          // bytes read (report ID first), 0 on timeout or error
    }

    // A Logitech HID++ 2.0 device that exposes feature 0x19b0 (HapticFeedback).
    // Platform-independent; used only from the MxHaptics worker thread.
    sealed class HidppHapticDevice : IDisposable
    {
        public const ushort LogitechVid = 0x046D;
        public const int LongLength = 20;
        const ushort HapticFeature = 0x19B0;
        const byte LongReport = 0x11;      // HID++ long report: 20 bytes
        const byte SoftwareId = 0x0B;      // low nibble of byte 3; tells our replies apart from other apps'
        public const int ReplyTimeoutMs = 300;

        // 0xFF = the device itself (Bluetooth); 1..6 = a slot on a Bolt / Unifying receiver.
        static readonly byte[] DeviceIndices = { 0xFF, 1, 2, 3, 4, 5, 6 };

        readonly IHidTransport hid;
        readonly byte[] report = new byte[LongLength];
        readonly byte[] reply = new byte[64];
        byte deviceIndex, featureIndex;

        HidppHapticDevice(IHidTransport hid) { this.hid = hid; }

        // Returns the first Logitech HID++ interface with a haptic engine, or null.
        public static HidppHapticDevice Find()
        {
            foreach (IHidTransport hid in HidTransport.OpenCandidates())
            {
                var device = new HidppHapticDevice(hid);   // owns hid from here
                try
                {
                    if (device.Resolve()) return device;
                }
                catch
                {
                    device.Dispose();
                    throw;
                }
                device.Dispose();
            }
            return null;
        }

        // IRoot (feature index 0) function 0 = GetFeature(featureId) -> index of that feature on the device.
        bool Resolve()
        {
            foreach (byte index in DeviceIndices)
            {
                if (MxHapticsClient.Stopping) return false;   // Editor domain reload: let the worker exit quickly
                Fill(index, 0x00, 0x00, (byte)(HapticFeature >> 8), (byte)(HapticFeature & 0xFF));
                if (!hid.Write(report)) continue;

                int deadline = Environment.TickCount + ReplyTimeoutMs;
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    if (left <= 0 || hid.Read(reply, left) < 5) break;
                    if (reply[1] != index) continue;
                    byte feature = reply[2];
                    // HID++ 1.0 (0x8F) / 2.0 (0xFF) error: bytes 3-4 echo our request (root index 0, function 0 | SoftwareId).
                    // Errors that echo another app's request are not ours.
                    if (feature == 0x8F || feature == 0xFF)
                    {
                        if (reply[3] == 0x00 && reply[4] == SoftwareId) break;
                        continue;
                    }
                    if (reply[0] != LongReport || feature != 0x00 || reply[3] != SoftwareId) continue;   // a notification or another app's reply
                    if (reply[4] == 0) break;   // root answered: feature not supported
                    deviceIndex = index;
                    featureIndex = reply[4];
                    return true;
                }
            }
            return false;
        }

        // HapticFeedback function 4 = play(waveformId). The reply is not awaited.
        public bool Play(byte waveform)
        {
            Fill(deviceIndex, featureIndex, 4, waveform, 0);
            return hid.Write(report);
        }

        void Fill(byte device, byte feature, byte function, byte p0, byte p1)
        {
            Array.Clear(report, 0, LongLength);
            report[0] = LongReport;
            report[1] = device;
            report[2] = feature;
            report[3] = (byte)((function << 4) | SoftwareId);
            report[4] = p0;
            report[5] = p1;
        }

        public void Dispose() => hid.Dispose();
    }
}
#endif
