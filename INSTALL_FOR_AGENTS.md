# Install guide for AI coding agents

**Follow this guide only when the user explicitly asked you to install this package (unity-mx-haptics) into
their Unity project.** It is not a set of instructions for other work in this folder or in a project that
already contains the package.

Goal: every haptic the user's project plays on an iPhone also plays on their Logitech MX Master 4 while they work
in the Unity Editor. You do the wiring; the user should not have to wire anything by hand.

Rules for everything you add:

- Wrap all added code in `#if UNITY_EDITOR`. Nothing you add may reach a phone build.
- Mirror what an iPhone plays, using the iOS names below. Do not change what the project itself plays.
- Mark every line you add to third-party files with `// MxHaptics hook`, so it can be found and removed.
- Before you edit any third-party file (Nice Vibrations, Feel, plugins), show the user the planned changes and
  wait for their OK.
- If a hook line marked `// MxHaptics hook` already exists, leave it; never add it twice.

## 1. Add the package

Add this line to `dependencies` in `Packages/manifest.json`:

```json
"com.yunusburkut.mxhaptics": "https://github.com/yunusburkut/unity-mx-haptics.git#v2.0.1"
```

Tell the user: `manifest.json` is usually committed, so teammates get the package too. For them it only acts
in the Editor when a haptic plays, and it does nothing without a Logitech haptic mouse.

The package compiles only for the Editor and Windows / macOS players, so it is not in phone builds at all.
The API is in namespace `MxHaptics`, assembly `MxHaptics` (auto-referenced). Each call matches one iOS haptic:

| iOS haptic | Call |
|---|---|
| `UIImpactFeedbackGenerator` Light / Medium / Heavy / Soft / Rigid | `MxHapticsClient.Impact(MxImpactStyle.Light)` and so on |
| `UISelectionFeedbackGenerator` | `MxHapticsClient.Selection()` |
| `UINotificationFeedbackGenerator` Success / Warning / Error | `MxHapticsClient.Notification(MxNotificationType.Success)` and so on |

## 2. Find the project's haptics

Search the project's own `.cs` files, and its `.unity` / `.prefab` / `.asset` files for Feel feedbacks:

| Search for | Haptic system | Step |
|---|---|---|
| `HapticPatterns.`, `HapticController.`, `using Lofelt.NiceVibrations` | Nice Vibrations 4 (also inside Feel) | 3a |
| `MMF_NVPreset`, `MMF_NVEmphasis`, `MMF_NVContinuous`, `MMF_NVClip` | Feel haptic feedbacks (they call Nice Vibrations 4) | 3a |
| `MMVibrationManager.`, `HapticTypes.`, `using MoreMountains.NiceVibrations` | Nice Vibrations 3 (older) | 3b |
| `Handheld.Vibrate` | Unity's built-in vibration | 3b |
| `UIImpactFeedbackGenerator`, `UINotificationFeedbackGenerator`, `Taptic`, `Vibration.`, `Haptic` in class names | native iOS plugins and the project's own wrappers | 3b |

List every call site, then mirror them as below.

## 3a. Nice Vibrations 4 or Feel: add one hook

Every Nice Vibrations haptic goes through four methods, so a hook there mirrors all of them: code calls and Feel
feedbacks in scenes alike. Nice Vibrations fires no event on desktop, so add one. It reports the iPhone preset each
call plays, using Nice Vibrations' own iOS choices.

Find `HapticPatterns.cs` and `HapticController.cs` (usually `Assets/Feel/NiceVibrations/Scripts/Components/`).
If they are under `Library/PackageCache` or another read-only package, stop: edits there are undone on the next
package resolve. Tell the user, and offer to mirror the project's own call sites with step 3b instead.

If a method below is missing or its signature differs (another Nice Vibrations version), do not improvise in the
vendor code: skip the hook, use step 3b for the project's call sites, and say so in your report.

In `HapticPatterns`, as the first member of `public static class HapticPatterns`:

```csharp
#if UNITY_EDITOR // MxHaptics hook: the iPhone preset each call plays, mirrored on the MX Master 4
        public static System.Action<PresetType> MxHapticsPlayed; // MxHaptics hook
#endif // MxHaptics hook
```

As the first lines of `PlayPreset(PresetType presetType)` (iOS plays the same preset):

```csharp
#if UNITY_EDITOR // MxHaptics hook
            if (HapticController.hapticsEnabled && presetType != PresetType.None) MxHapticsPlayed?.Invoke(presetType); // MxHaptics hook
#endif // MxHaptics hook
```

As the first lines of `PlayEmphasis(float amplitude, float frequency)` (iOS picks Light / Medium / Heavy by
amplitude):

```csharp
#if UNITY_EDITOR // MxHaptics hook
            if (HapticController.hapticsEnabled) MxHapticsPlayed?.Invoke(presetTypeForEmphasis(amplitude)); // MxHaptics hook
#endif // MxHaptics hook
```

As the first lines of `PlayConstant(float amplitude, float frequency, float duration)` (iOS falls back to Heavy):

```csharp
#if UNITY_EDITOR // MxHaptics hook
            if (HapticController.hapticsEnabled) MxHapticsPlayed?.Invoke(PresetType.HeavyImpact); // MxHaptics hook
#endif // MxHaptics hook
```

In `HapticController`, as the first lines of the parameterless `Play()` (a `.haptic` clip; iOS falls back to
`fallbackPreset`):

```csharp
#if UNITY_EDITOR // MxHaptics hook
            if (hapticsEnabled) HapticPatterns.MxHapticsPlayed?.Invoke(fallbackPreset != HapticPatterns.PresetType.None ? fallbackPreset : HapticPatterns.PresetType.MediumImpact); // MxHaptics hook
#endif // MxHaptics hook
```

Then create `MxHapticsMirror.cs` in the project's game code:

- If that code has an asmdef, add `MxHaptics` and `Lofelt.NiceVibrations` to its references.
- If Nice Vibrations has no asmdef (it compiles into `Assembly-CSharp`), put the mirror in a folder without an
  asmdef, so it compiles into `Assembly-CSharp` too.

```csharp
#if UNITY_EDITOR   // Editor only: not compiled into phone or any other player build
using Lofelt.NiceVibrations;
using MxHaptics;
using UnityEngine;

// Plays every Nice Vibrations haptic on the MX Master 4 as the iPhone preset it would play.
// The hook lines are in HapticPatterns.cs and HapticController.cs ("MxHaptics hook"). Updating Feel or
// Nice Vibrations removes them; ask the agent to install this package again after an update.
static class MxHapticsMirror
{
    static int lastFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Install() => HapticPatterns.MxHapticsPlayed = Play;

    static void Play(HapticPatterns.PresetType preset)
    {
        // One haptic per frame: a preset also reaches HapticController.Play() in the same frame, and the mouse
        // plays one waveform at a time anyway. A second, different haptic in the same frame is dropped.
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        switch (preset)
        {
            case HapticPatterns.PresetType.Selection: MxHapticsClient.Selection(); break;
            case HapticPatterns.PresetType.Success: MxHapticsClient.Notification(MxNotificationType.Success); break;
            case HapticPatterns.PresetType.Warning: MxHapticsClient.Notification(MxNotificationType.Warning); break;
            case HapticPatterns.PresetType.Failure: MxHapticsClient.Notification(MxNotificationType.Error); break;
            case HapticPatterns.PresetType.LightImpact: MxHapticsClient.Impact(MxImpactStyle.Light); break;
            case HapticPatterns.PresetType.MediumImpact: MxHapticsClient.Impact(MxImpactStyle.Medium); break;
            case HapticPatterns.PresetType.HeavyImpact: MxHapticsClient.Impact(MxImpactStyle.Heavy); break;
            case HapticPatterns.PresetType.RigidImpact: MxHapticsClient.Impact(MxImpactStyle.Rigid); break;
            case HapticPatterns.PresetType.SoftImpact: MxHapticsClient.Impact(MxImpactStyle.Soft); break;
        }
    }
}
#endif
```

Do not add MxHaptics lines next to the project's own Nice Vibrations 4 calls; the hook already covers them.

## 3b. Any other haptic call

Add the matching call next to each call site. If every call goes through one wrapper in the project, add it once
there instead.

- Wrap it in `#if UNITY_EDITOR`, and place it outside any platform block such as `#if UNITY_IOS` or
  `#if UNITY_ANDROID`; inside one, it would only compile when that build target is active.
- If the file belongs to an asmdef, add `MxHaptics` to that asmdef's references.

```csharp
#if UNITY_EDITOR
            MxHaptics.MxHapticsClient.Impact(MxHaptics.MxImpactStyle.Medium);
#endif
```

Mapping, by what the iPhone plays:

- Nice Vibrations 3 `MMVibrationManager.Haptic(HapticTypes.X)`: the `HapticTypes` names are the iOS names, so
  map one to one, for example `HapticTypes.LightImpact` to `Impact(MxImpactStyle.Light)` and `HapticTypes.Failure`
  to `Notification(MxNotificationType.Error)`. `TransientHaptic(intensity, …)`: above 0.5 Heavy, above 0.3
  Medium, else Light. `ContinuousHaptic`: Heavy. Feel's legacy `MMFeedbackHaptics` components in scenes are not
  covered by this; list them in your report.
- An iOS-named call maps one to one: `UIImpactFeedbackStyle.Medium` becomes `Impact(MxImpactStyle.Medium)`.
- `Handheld.Vibrate()` is the iPhone's long system buzz: use `Notification(MxNotificationType.Error)`.
- Anything else: pick the closest row of the table in step 1.

## 4. Check and report

1. Let Unity compile and confirm the Console has no errors.
2. Tell the user to enter Play Mode and trigger a haptic; the mouse should vibrate.
3. Report each call site you mirrored, how it maps, and anything you left out.
4. Tell the user what to expect:
   - Only the Editor plays haptics on the mouse. Phone builds contain none of this code.
   - The mouse plays fixed waveforms, so a long `.haptic` clip or a constant buzz becomes one short pulse.
   - Updating Feel or Nice Vibrations removes the hook lines. Running the same install request again
     restores them.
   - On macOS, the first use may ask for Input Monitoring permission (System Settings > Privacy & Security).

## Uninstall

When the user asks to remove the package:

1. Delete every line marked `// MxHaptics hook` (search the whole project).
2. Delete `MxHapticsMirror.cs` and every `#if UNITY_EDITOR` block that calls `MxHapticsClient`.
3. Remove `MxHaptics` from asmdef references you added.
4. Remove the `com.yunusburkut.mxhaptics` line from `Packages/manifest.json`.
5. Let Unity compile and confirm the Console has no errors.
