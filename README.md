# MX Master 4 Haptics for Unity

Make your Logitech MX Master 4 mouse vibrate from Unity. No extra software needed.

## Install with Claude Code (mirror your phone haptics)

Making a mobile game with haptics? Tell Claude Code (or any AI coding agent), in any language:

```
Install https://github.com/yunusburkut/unity-mx-haptics into my project so it mirrors my project's haptics one to one.
```

The agent follows [AGENTS.md](AGENTS.md):

1. It finds every haptic in your project: Nice Vibrations, Feel feedbacks, `Handheld.Vibrate` and native iOS calls.
2. It adds an Editor-only mirror, so each one plays on the mouse as the iPhone haptic it would play.
3. Your phone builds get none of it.

## Install by hand

In Unity: **Window → Package Manager → + → Add package from git URL**, then paste:

```
https://github.com/yunusburkut/unity-mx-haptics.git#v2.0.0
```

Drop `#v2.0.0` to always get the latest version.

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

- Works in the Editor and in Windows and macOS builds. Phone, console and WebGL builds don't include the package,
  so wrap direct calls in shared code with `#if UNITY_EDITOR || UNITY_STANDALONE`.
- macOS may ask for **Input Monitoring** permission once (System Settings → Privacy & Security).
- No mouse connected? Nothing happens, no errors.
- Call it on events, not every frame.

MIT License. Protocol details from [OpenLogi](https://github.com/AprilNEA/OpenLogi).
