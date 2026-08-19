# No Rest for the Wicked — Third Person Camera

[中文说明](README_CN.md)

A third-person camera mod for **No Rest for the Wicked** (BepInEx IL2CPP plugin). Press **F9** to switch between the default top-down camera and a fully playable third-person camera.

![Demo](demo/demo.gif)

## Background

The game ships with a built-in but **unfinished** third-person mode. Out of the box it is barely playable: mouse sensitivity is 0 (camera won't turn), movement stays world-aligned (W always walks "north" no matter where you look), and the camera snaps violently at walls. This mod completes it.

## Features

- **F9** toggles TopDown / ThirdPerson at any time
- Working mouse look (horizontal + vertical sensitivity, direct input)
- Camera-relative movement — W always walks toward where the camera faces
- Spring-arm smoothing — no more jitter when the camera collides with walls
- All values configurable in `BepInEx/config/com.nrtw.thirdpersoncam.cfg`, applied live

## How it works

- Rewrites the game's own `ThirdPersonCameraConfig` asset in memory (mouse sensitivity, direct input, follow/smoothing speeds)
- A Harmony postfix on `QuantumLocalInputSource.UpdateRInputQuantumInput` rotates the movement input direction by the camera yaw, so movement becomes camera-relative
- A `Camera.onPreCull` callback SmoothDamps the camera position, turning the game's instant collision snap into a smooth spring-arm pull-in

All changes are in-memory only — no game files or saves are touched.

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

## Usage

- **F9** switches between top-down and third-person.
- In third-person: move the mouse to orbit the camera, WASD moves relative to the camera.
- Tune sensitivity, follow speed and smoothing in `BepInEx/config/com.nrtw.thirdpersoncam.cfg` — changes apply immediately.

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
