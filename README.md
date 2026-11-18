# MX Master 4 Haptics for Unity

Make your Logitech MX Master 4 mouse vibrate from Unity. No extra software needed.

## Install

In Unity: **Window → Package Manager → + → Add package from git URL**, then paste:

```
https://github.com/yunusburkut/unity-mx-haptics.git#v1.1.0
```

Drop `#v1.1.0` to always get the latest version.

## Use

```csharp
using MxHaptics;

MxHapticsClient.Impact(MxImpactStyle.Heavy);              // a hit
MxHapticsClient.Selection();                              // a menu tick
MxHapticsClient.Notification(MxNotificationType.Success); // a win
```

The names match iOS haptics. You can also play any of the mouse's 16 waveforms:

```csharp
MxHapticsClient.Play(MxWaveform.Firework);
```

## Good to know

- Works on Windows and macOS. On other platforms the calls do nothing, so your game still builds.
- macOS may ask for **Input Monitoring** permission once (System Settings → Privacy & Security).
- No mouse connected? Nothing happens, no errors.
- Call it on events, not every frame.

MIT License. Protocol details from [OpenLogi](https://github.com/AprilNEA/OpenLogi).
