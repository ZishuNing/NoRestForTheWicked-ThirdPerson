# 恶意不息 — 第三人称相机

[English](README.md)

《恶意不息》（No Rest for the Wicked）的第三人称相机 Mod，按 **F9** 在默认俯视角和第三人称视角之间切换。

![演示](demo/demo.gif)

## 功能

- **F9** 随时切换俯视角 / 第三人称
- 可用的鼠标视角（水平/垂直灵敏度）
- 相机相对移动
- 弹簧臂平滑——相机撞墙不再抖动
- 所有数值可在 `BepInEx/config/com.nrtw.thirdpersoncam.cfg` 调整，即时生效

## 实现原理

- 在内存中改写游戏自带的 `ThirdPersonCameraConfig` 资产（鼠标灵敏度、直接输入、跟随/平滑速度）
- 通过 Harmony postfix 挂钩 `QuantumLocalInputSource.UpdateRInputQuantumInput`，把移动输入方向按相机 yaw 旋转，使移动变为相机相对
- 通过 `Camera.onPreCull` 回调对相机位置做 SmoothDamp，把游戏原本的瞬时碰撞回弹变成平滑的弹簧臂收缩

所有改动仅作用于内存——不修改任何游戏文件和存档。

## 适配版本

| 项目 | 版本 |
| --- | --- |
| 游戏 | Steam 版《恶意不息》，2026-08 当时最新版本 |
| 引擎 | Unity 6000.1.15f1（IL2CPP，x64） |
| BepInEx | 6.0.0-be.785（IL2CPP bleeding edge，win-x64） |

其他版本未测试。若游戏更新导致 Mod 失效，请提 Issue。

## 安装

1. 从 <https://builds.bepinex.dev/projects/bepinex_be> 下载 `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.*.zip`，解压到游戏根目录，启动一次游戏让 BepInEx 完成初始化后退出。
2. 从 [Releases](../../releases) 下载 `NRTWThirdPersonCam.zip`，解压到 `BepInEx\plugins\`。
3. 启动游戏，读取存档后按 **F9**。

## 卸载

删除 `BepInEx\plugins\NRTWThirdPersonCam\`。完全卸载 BepInEx：删除游戏根目录下的 `winhttp.dll`、`doorstop_config.ini`、`dotnet\`、`BepInEx\`。

## 从源码构建

前置：.NET 6 SDK + 本机已初始化 BepInEx 的游戏。

```bash
cd src/NRTWThirdPersonCam
dotnet build -c Release -p:GameDir="<游戏根目录>"
```

产物：`bin/Release/net6.0/NRTWThirdPersonCam.dll` → 拷到 `BepInEx\plugins\NRTWThirdPersonCam\`。

## 参与开发

欢迎 Issue 和 PR。报告问题时请附上 `BepInEx\LogOutput.log` 中 `[NRTW3P]` 相关段落。

## 免责声明

本项目与 Moon Studios 无关。仅限单人使用，风险自负。
