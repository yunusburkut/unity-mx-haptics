using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MxHaptics
{
    // Names must match LogiPlugin/UnityHapticsPlugin.cs and its events YAML.
    public enum MxWaveform
    {
        SharpCollision, DampCollision, SubtleCollision,
        SharpStateChange, DampStateChange,
        Knock, Ringing, Jingle,
        Completed, HappyAlert, AngryAlert,
    }

    // Sends one UDP packet per call to the UnityHaptics Logi plugin on 127.0.0.1:47800.
    // Fire and forget: if the plugin or the mouse is missing, nothing happens.
    public static class MxHapticsClient
    {
        public const int Port = 47800;

        static readonly string[] Names =
        {
            "sharp_collision", "damp_collision", "subtle_collision",
            "sharp_state_change", "damp_state_change",
            "knock", "ringing", "jingle",
            "completed", "happy_alert", "angry_alert",
        };

#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        static readonly IPEndPoint Endpoint = new IPEndPoint(IPAddress.Loopback, Port);
        static byte[][] payloads;   // encoded once, so Play() does not allocate
        static UdpClient udp;

        public static void Play(MxWaveform waveform)
        {
            try
            {
                if (udp == null)
                {
                    udp = new UdpClient();
                    payloads = new byte[Names.Length][];
                    for (int i = 0; i < Names.Length; i++) payloads[i] = Encoding.ASCII.GetBytes(Names[i]);
                }
                byte[] p = payloads[(int)waveform];
                udp.Send(p, p.Length, Endpoint);
            }
            catch (SocketException) { } // plugin not running: haptics are optional
        }
#else
        public static void Play(MxWaveform waveform) { } // the Logi plugin exists only on Windows and macOS
#endif
    }
}
