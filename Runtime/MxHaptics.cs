using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MxHaptics
{
    // HID++ 0x19b0 waveform IDs, as reverse-engineered by OpenLogi (github.com/AprilNEA/OpenLogi, MIT).
    public enum MxWaveform : byte
    {
        SharpStateChange = 0,
        DampStateChange = 1,
        SharpCollision = 2,
        DampCollision = 3,
        SubtleCollision = 4,
        HappyAlert = 5,
        AngryAlert = 6,
        Completed = 7,
        Square = 8,
        Wave = 9,
        Firework = 10,
        Mad = 11,
        Knock = 12,
        Jingle = 13,
        Ringing = 14,
        WhisperCollision = 27,
    }

    // Same names as iOS UIImpactFeedbackGenerator / UINotificationFeedbackGenerator.
    public enum MxImpactStyle { Light, Medium, Heavy, Soft, Rigid }
    public enum MxNotificationType { Success, Warning, Error }

    // Plays MX Master 4 haptic waveforms by writing HID++ reports straight to the mouse.
    // No Logi Options+ needed. Fire and forget: without a haptic mouse, Play does nothing.
    // Windows (WindowsHid.cs) and macOS (MacHid.cs); a no-op on every other platform.
    public static class MxHapticsClient
    {
        static readonly ConcurrentQueue<MxWaveform> queue = new ConcurrentQueue<MxWaveform>();
        static readonly AutoResetEvent signal = new AutoResetEvent(false);
        static readonly object startLock = new object();
        static Thread worker;
        static volatile bool stopping;

        // iOS-style API. The mouse firmware only plays its fixed waveforms (no custom amplitude curves),
        // so each iOS call maps to the closest one; it is not a 1:1 copy of the Taptic Engine feel.
        // Tune the feel by editing these two tables.
        static readonly MxWaveform[] ImpactMap =
        {
            MxWaveform.SubtleCollision,    // Light
            MxWaveform.DampCollision,      // Medium
            MxWaveform.SharpCollision,     // Heavy
            MxWaveform.DampStateChange,    // Soft
            MxWaveform.SharpStateChange,   // Rigid
        };
        static readonly MxWaveform[] NotificationMap =
        {
            MxWaveform.HappyAlert,   // Success
            MxWaveform.Knock,        // Warning
            MxWaveform.AngryAlert,   // Error
        };

        public static void Impact(MxImpactStyle style) => Play(ImpactMap[(int)style]);
        public static void Selection() => Play(MxWaveform.WhisperCollision);   // UISelectionFeedbackGenerator
        public static void Notification(MxNotificationType type) => Play(NotificationMap[(int)type]);

        static int warned;

        // One warning per session, so a missing permission or a broken driver cannot flood the Console.
        internal static void WarnOnce(string message)
        {
            if (Interlocked.Exchange(ref warned, 1) != 0) return;
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning("MxHaptics: " + message);
#endif
        }

        // Safe to call from any thread, every frame if needed; the HID write happens on a worker thread.
        public static void Play(MxWaveform waveform)
        {
#if UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN) || MXHAPTICS_WINDOWS || UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX) || MXHAPTICS_MAC
            queue.Enqueue(waveform);
            EnsureWorker();
            signal.Set();
#endif
        }

#if UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN) || MXHAPTICS_WINDOWS || UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX) || MXHAPTICS_MAC
        const int RediscoverDelayMs = 3000;   // after a failed search, do not enumerate HID devices again for this long

        static void EnsureWorker()
        {
            if (worker != null) return;
            lock (startLock)
            {
                if (worker != null) return;
                stopping = false;
                AppDomain.CurrentDomain.DomainUnload += (s, e) => Stop();   // Editor domain reload
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
                worker = new Thread(Run) { IsBackground = true, Name = "MxHaptics" };
                worker.Start();
            }
        }

        static void Stop()
        {
            stopping = true;
            signal.Set();
            worker?.Join(500);
        }

        static void Run()
        {
            HidppHapticDevice device = null;
            int nextSearch = Environment.TickCount;   // not 0: TickCount is negative after ~24.9 days of uptime
            try
            {
                while (!stopping)
                {
                    signal.WaitOne();
                    while (!stopping && queue.TryDequeue(out var waveform))
                    {
                        try
                        {
                            if (device != null && device.Play((byte)waveform)) continue;

                            // First play, or the mouse was unplugged / fell asleep: search, then retry this play once.
                            device?.Dispose();
                            device = null;
                            if (Environment.TickCount - nextSearch < 0) continue;   // backing off: drop the play
                            device = HidppHapticDevice.Find();
                            while (queue.TryDequeue(out _)) { }   // plays queued during the search are stale
                            if (device == null || !device.Play((byte)waveform))
                            {
                                device?.Dispose();
                                device = null;
                                nextSearch = Environment.TickCount + RediscoverDelayMs;
                            }
                        }
                        catch (Exception e)   // keep the worker alive; a dead worker would let the queue grow forever
                        {
                            device?.Dispose();
                            device = null;
                            nextSearch = Environment.TickCount + RediscoverDelayMs;
                            WarnOnce("haptics paused until the mouse is found again: " + e);
                        }
                    }
                }
            }
            finally
            {
                device?.Dispose();
            }
        }
#endif
    }
}
