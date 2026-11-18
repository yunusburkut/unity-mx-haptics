namespace Loupedeck.UnityHapticsPlugin
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    // Listens on 127.0.0.1:47800 for UDP packets whose payload is a waveform name
    // (e.g. "sharp_collision") and raises the haptic event of the same name.
    // events/extra/eventMapping.yaml maps each event to its waveform.
    public class UnityHapticsPlugin : Plugin
    {
        public const Int32 Port = 47800;

        // Must match package/events/DefaultEventSource.yaml and the Unity MxWaveform enum.
        private static readonly String[] Waveforms =
        {
            "sharp_collision", "damp_collision", "subtle_collision",
            "sharp_state_change", "damp_state_change",
            "knock", "ringing", "jingle",
            "completed", "happy_alert", "angry_alert",
        };

        private UdpClient _udp;
        private CancellationTokenSource _cts;

        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public override void Load()
        {
            try
            {
                foreach (var name in Waveforms)
                {
                    this.PluginEvents.AddEvent(name, name, $"Unity requested the {name} waveform");
                }

                this._cts = new CancellationTokenSource();
                this._udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
                _ = this.ListenAsync(this._cts.Token);
                this.Log.Info($"Listening for Unity haptics on 127.0.0.1:{Port}");
            }
            catch (Exception ex)
            {
                this.Log.Error(ex, "UnityHaptics failed to start");
            }
        }

        public override void Unload()
        {
            this._cts?.Cancel();
            this._udp?.Dispose();
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var packet = await this._udp.ReceiveAsync(token);
                    // Any local process can send here: only raise names we registered.
                    var name = Encoding.ASCII.GetString(packet.Buffer).Trim();
                    if (Array.IndexOf(Waveforms, name) >= 0)
                    {
                        this.PluginEvents.RaiseEvent(name);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    this.Log.Warning(ex, "Haptics UDP receive failed");
                }
            }
        }
    }
}
