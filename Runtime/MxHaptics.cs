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
    // No Logi Options+ needed. Fire and forget: without a haptic mouse, nothing happens.
    // Windows (WindowsHid.cs) and macOS (MacHid.cs). The asmdef leaves the package out of every other player
    // build; in the Linux Editor the calls do nothing.
    public static class MxHapticsClient
    {
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

        public static void Impact(MxImpactStyle style)
        {
            if ((uint)style < (uint)ImpactMap.Length) Play(ImpactMap[(int)style]);
        }

        public static void Selection() => Play(MxWaveform.WhisperCollision);   // UISelectionFeedbackGenerator

        public static void Notification(MxNotificationType type)
        {
            if ((uint)type < (uint)NotificationMap.Length) Play(NotificationMap[(int)type]);
        }

        // Safe to call from any thread. Call it on events, not every frame: the mouse plays one waveform at a
        // time, and plays beyond MaxPending are dropped rather than queued up to lag behind the game.
        public static void Play(MxWaveform waveform)
        {
#if !UNITY_EDITOR_LINUX
            if (queue.Count >= MaxPending) return;
            queue.Enqueue(waveform);
            EnsureWorker();
            signal.Set();
#endif
        }

        static int warned;

        // One warning per session, so a missing permission or a broken driver cannot flood the Console.
        internal static void WarnOnce(string message)
        {
            if (Interlocked.Exchange(ref warned, 1) != 0) return;
            UnityEngine.Debug.LogWarning("MxHaptics: " + message);
        }

#if !UNITY_EDITOR_LINUX
        const int MaxPending = 4;
        // After a failed search, wait before enumerating HID devices again: 3 s, doubling up to 60 s, so a
        // machine without the mouse (or with only other Logitech devices) is not probed over and over.
        // Ceiling: a mouse connected later can take up to 60 s to be found.
        const int MinRetryDelayMs = 3000, MaxRetryDelayMs = 60000;

        static readonly ConcurrentQueue<MxWaveform> queue = new ConcurrentQueue<MxWaveform>();
        static readonly AutoResetEvent signal = new AutoResetEvent(false);
        static readonly object startLock = new object();
        static Thread worker;
        static volatile bool stopping;

        internal static bool Stopping => stopping;

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
            int retryDelay = MinRetryDelayMs;

            void GiveUp()
            {
                device?.Dispose();
                device = null;
                nextSearch = Environment.TickCount + retryDelay;
                retryDelay = Math.Min(retryDelay * 2, MaxRetryDelayMs);
            }

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
                            if (device != null && device.Play((byte)waveform)) retryDelay = MinRetryDelayMs;
                            else GiveUp();
                        }
                        catch (Exception e)   // keep the worker alive; a dead worker would stop all haptics
                        {
                            GiveUp();
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
