# MX Master 4 Haptics for Unity

Make your Logitech MX Master 4 mouse vibrate from Unity. No extra software needed.

## Install

In Unity: **Window → Package Manager → + → Add package from git URL**, then paste:

```
https://github.com/yunusburkut/unity-mx-haptics.git#v1.1.1
```

Drop `#v1.1.1` to always get the latest version.

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

## Make your own haptics

Give your game its own haptic names. Put this anywhere in your project; no package changes needed:

```csharp
using MxHaptics;

public static class GameHaptics
{
    public static void Jump()  => MxHapticsClient.Impact(MxImpactStyle.Light);
    public static void Coin()  => MxHapticsClient.Play(MxWaveform.Completed);
    public static void Boss()  => MxHapticsClient.Play((MxWaveform)20);   // any ID 0-255, even ones not listed
}
```

An ID the mouse doesn't know just does nothing. The next haptic still plays.

## Change the package

It's open source (MIT). To edit it inside your project:

1. Remove the git URL package in Package Manager.
2. Clone the repo into your project's `Packages` folder:
   ```
   git clone https://github.com/yunusburkut/unity-mx-haptics.git Packages/com.yunusburkut.mxhaptics
   ```
3. Edit any file. Unity recompiles it like your own code.

To change what `Impact` and `Notification` feel like, edit the two tables at the top of `Runtime/MxHaptics.cs`.
Fork the repo to share your version, and pull requests are welcome.

## Good to know

- Works on Windows and macOS. On other platforms the calls do nothing, so your game still builds.
- macOS may ask for **Input Monitoring** permission once (System Settings → Privacy & Security).
- No mouse connected? Nothing happens, no errors.
- Call it on events, not every frame.

MIT License. Protocol details from [OpenLogi](https://github.com/AprilNEA/OpenLogi).
