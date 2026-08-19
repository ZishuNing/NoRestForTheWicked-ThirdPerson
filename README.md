# No Rest for the Wicked — Third Person Camera

[中文说明](README_CN.md)

A third-person camera mod for **No Rest for the Wicked**. Press **F9** to switch between the default top-down camera and third-person.

![Demo](demo/demo.gif)

## Features

- **F9** toggles top-down / third-person at any time
- Working mouse look (horizontal + vertical sensitivity)
- Camera-relative movement
- Spring-arm smoothing — no more camera jitter at walls
- All values configurable in `BepInEx/config/com.nrtw.thirdpersoncam.cfg`, applied live

## How it works

- Rewrites the game's own third-person config asset in memory (sensitivity, follow and smoothing speeds)
- Patches movement input to follow the camera yaw
- SmoothDamps the camera position on wall collisions

Everything is in-memory only — no game files or saves are touched.

## Compatibility

| Component | Version |
| --- | --- |
| Game | No Rest for the Wicked (Steam), latest build as of 2026-08 |
| Engine | Unity 6000.1.15f1 (IL2CPP, x64) |
| BepInEx | 6.0.0-be.785 (IL2CPP bleeding edge, win-x64) |

Other versions are untested. If a game update breaks the mod, please open an Issue.

## Installation

1. Download `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.*.zip` from <https://builds.bepinex.dev/projects/bepinex_be>, extract it into the game root, launch the game once so BepInEx finishes first-time setup, then quit.
2. Download `NRTWThirdPersonCam.zip` from [Releases](../../releases) and extract it into `BepInEx\plugins\`.
3. Launch the game, load a save, press **F9**.

## Uninstall

Delete `BepInEx\plugins\NRTWThirdPersonCam\`. To remove BepInEx entirely, delete `winhttp.dll`, `doorstop_config.ini`, `dotnet\`, and `BepInEx\` from the game root.

## Build from Source

Requires .NET 6 SDK and a local game install with BepInEx initialized.

```bash
cd src/NRTWThirdPersonCam
dotnet build -c Release -p:GameDir="<game root>"
```

Output: `bin/Release/net6.0/NRTWThirdPersonCam.dll` → copy to `BepInEx\plugins\NRTWThirdPersonCam\`.

## Contributing

Issues and PRs are welcome. When reporting a problem, attach the `[NRTW3P]` lines from `BepInEx\LogOutput.log`.

## Disclaimer

Not affiliated with Moon Studios. Single-player use only. Use at your own risk.
