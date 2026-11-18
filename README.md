# unity-mx-haptics

Feel Unity game events on the haptic motor of a Logitech **MX Master 4**.

```
Unity  --UDP 127.0.0.1:47800-->  UnityHaptics Logi plugin  --PluginEvents.RaiseEvent-->  Logi Options+  -->  MX Master 4
```

Unity cannot reach the mouse directly. A small Logi Actions SDK plugin runs inside Logi Plugin Service,
listens for a waveform name on local UDP and raises the haptic event of the same name.

## Requirements

- MX Master 4 with current firmware, Logi Options+ (Plugin Service 6.x)
- .NET 10 SDK (to build the plugin)
- Windows or macOS. Other platforms compile to a no-op.

## 1. Install the Logi plugin

```bash
dotnet build LogiPlugin -c Debug
```

The build links the output into Logi Plugin Service and reloads it. Check
`%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\UnityHaptics.log` for
`Listening for Unity haptics on 127.0.0.1:47800`.

Quick test without Unity (PowerShell):

```powershell
$u=New-Object Net.Sockets.UdpClient; $b=[Text.Encoding]::ASCII.GetBytes('sharp_collision'); $u.Send($b,$b.Length,'127.0.0.1',47800)
```

## 2. Add the Unity package

Package Manager → Add package from git URL:

```
https://github.com/yunusburkut/unity-mx-haptics.git?path=/UnityPackage
```

```csharp
using MxHaptics;

MxHapticsClient.Play(MxWaveform.SharpCollision); // hit
MxHapticsClient.Play(MxWaveform.Jingle);         // level complete
```

`Play` does not allocate after the first call and does nothing if the plugin is not running.

## Waveforms

| Enum | Use |
|---|---|
| `SharpCollision`, `DampCollision`, `SubtleCollision` | hits, landings, light ticks |
| `SharpStateChange`, `DampStateChange` | something turned on / off |
| `Knock`, `Ringing` | attention |
| `Jingle`, `Completed`, `HappyAlert` | rewards, task done |
| `AngryAlert` | damage, failure |

To add or remap one, edit `LogiPlugin/package/events/*.yaml`, the `Waveforms` array in
`UnityHapticsPlugin.cs` and the `MxWaveform` enum together.

## Notes

- Haptics work only on a machine where this plugin is installed; players need it too.
- No rate limit: calling `Play` every frame floods the motor. Call it on events.
- Based on Logitech's [HapticsPlugin sample](https://github.com/Logitech/actions-sdk). Unlike the sample,
  `pluginFolderWin: bin` must be set or the service fails with `Parameter 'fileName'`.

MIT License.
