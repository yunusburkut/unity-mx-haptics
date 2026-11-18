# unity-mx-haptics

Feel Unity game events on the haptic motor of a Logitech **MX Master 4**. Install the package and call it:
no Logi Options+, no plugin, no native DLL.

```
MxHapticsClient.Play()  ->  worker thread  ->  HID++ 2.0 report (feature 0x19b0)  ->  MX Master 4
```

The package writes HID++ reports straight to the mouse through the Windows HID API (`hid.dll`, P/Invoke).
It works over Bluetooth and over a Bolt / Unifying receiver.

## Install

Package Manager → **Add package from git URL**:

```
https://github.com/yunusburkut/unity-mx-haptics.git?path=/UnityPackage
```

## Use

iOS-style API (same names as `UIImpactFeedbackGenerator`, `UISelectionFeedbackGenerator`,
`UINotificationFeedbackGenerator`):

```csharp
using MxHaptics;

MxHapticsClient.Impact(MxImpactStyle.Heavy);              // hit
MxHapticsClient.Selection();                              // menu tick
MxHapticsClient.Notification(MxNotificationType.Success); // level complete
```

Or a raw firmware waveform:

```csharp
MxHapticsClient.Play(MxWaveform.Firework);
```

Calls are fire and forget and safe from any thread. Without a haptic mouse they do nothing.

## iOS mapping

The mouse plays only its 16 built-in waveforms; custom amplitude curves are not possible.
Each iOS call maps to the closest waveform, so the feel is similar, not identical.
Edit `ImpactMap` / `NotificationMap` in `MxHaptics.cs` to tune it.

| iOS | MX Master 4 waveform |
|---|---|
| Impact Light / Medium / Heavy | `SubtleCollision` / `DampCollision` / `SharpCollision` |
| Impact Soft / Rigid | `DampStateChange` / `SharpStateChange` |
| Selection | `WhisperCollision` |
| Notification Success / Warning / Error | `HappyAlert` / `Knock` / `AngryAlert` |

## Limits

- Windows only (Editor and standalone player). Other platforms compile to a no-op; macOS needs an IOKit backend.
- The HID++ haptic feature (0x19b0) is undocumented. Function numbers and waveform IDs come from
  [OpenLogi](https://github.com/AprilNEA/OpenLogi) (MIT). A firmware update could change them.
- No rate limit: a call every frame floods the motor. Call it on events.

MIT License.
