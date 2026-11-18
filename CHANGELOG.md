# Changelog

## 2.0.2

- macOS: opens only Logitech mice and HID++ receiver interfaces; Logitech keyboards (which made macOS ask for Input Monitoring even without the mouse, despite the 2.0.1 note), wheels and headsets are skipped.
- macOS: the device list is released when the worker stops, so Editor domain reloads no longer leak one each.
- Editor domain reload no longer logs a false "haptics paused" warning, and cannot free a device's memory twice.
- Agent guide: skips wrappers the Nice Vibrations hook already covers (no double plays), warns that git packages need `git` on build machines, marks every added line for uninstall, keeps the mirror out of `Editor` folders, and says the mirror is the closest preset, not an exact copy.
- `package.json`: changelog, license and repository links.

## 2.0.1

- macOS: no longer opens every HID device at once.
- Windows: other vendors' HID devices are skipped before opening (by path).
- Editor domain reload during a device search can no longer free memory that Windows is still writing to.
- Failed searches back off from 3 s up to 60 s; at most 4 plays wait in line, extra ones are dropped.
- Out-of-range `MxImpactStyle` / `MxNotificationType` values do nothing instead of throwing.
- Agent guide renamed to `INSTALL_FOR_AGENTS.md`: asks before editing third-party files, handles read-only packages, asmdefs, Nice Vibrations 3, re-installs and uninstall.
- README: the `#if` for shared code now excludes Linux players.

## 2.0.0

- `INSTALL_FOR_AGENTS.md` (then `AGENTS.md`): an AI agent mirrors a project's phone haptics on the mouse, Editor only.
- The package compiles only for the Editor and Windows / macOS players. Breaking: calls in phone builds need `#if`.

## 1.1.1

- README: custom haptics and editing the package.

## 1.1.0

- macOS support.

## 1.0.0

- First release: Windows, iOS-style API, no Logi Options+ needed.
