using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Moon.Forsaken;
using Moon.TheLoop;
using Photon.Deterministic;
using Quantum;
using UnityEngine;
using Time = UnityEngine.Time;
using Input = UnityEngine.Input;

namespace NRTWThirdPersonCam;

// 第三人称相机手感修正插件。
// 背景：游戏内置第三人称模式（设置存档 CameraMode=1，或 F9 切换）是未完成的实验功能：
//   - 配置资产里 MouseSensitivityX/Y = 0 → 键鼠完全转不了视角（手柄 180°/s 正常）
//   - UseDirectInput = false，视角输入走 RotationLagSpeed 平滑
//   - 角色位置/朝向被相机系统二次平滑（CharacterPosition/RotationSmoothSpeed）
//   - 撞墙回弹无平滑（v1.2 起由 onPreCull 弹簧臂平滑修复）
//   - 移动输入方向基准恒为俯视世界方向，不随相机 yaw 旋转（v1.4 起由
//     MovementDirectionPatch 修复，根因见 _camera_research/06_thirdperson_switches_and_fixes.md）
// v1.1：修复切换世界后 F9 失效、移动输入退回世界坐标的问题——
//   PlayerCamera.All 静态列表在切世界后残留已销毁的旧相机，原查找逻辑不检查存活，
//   反复抓到同一个死对象，Update 在空分支提前 return，F9 永远轮询不到。
//   现在查找时逐个过滤死对象，热键轮询也挪到不依赖相机状态的位置。
// 本插件在游戏运行时改写 ThirdPersonCameraConfig（ScriptableObject 单例，仅改内存，
// 不动游戏文件），并提供热键切换相机模式。所有数值可在
// BepInEx/config/com.nrtw.thirdpersoncam.cfg 里调，即时生效。
[BepInPlugin("com.nrtw.thirdpersoncam", "NRTW Third Person Camera Tweaks", "1.1.0")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> EnableTweaks;
    internal static ConfigEntry<float> MouseSensX;
    internal static ConfigEntry<float> MouseSensY;
    internal static ConfigEntry<bool> DirectInput;
    internal static ConfigEntry<float> MovementLag;
    internal static ConfigEntry<float> CharPosSmooth;
    internal static ConfigEntry<float> CharRotSmooth;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<bool> FixMovementMapping;
    internal static ConfigEntry<bool> SmoothCameraPosition;
    internal static ConfigEntry<float> CamPosSmoothTime;

    public override void Load()
    {
        Log = base.Log;
        try
        {
            var c = Config;
            EnableTweaks = c.Bind("Tweaks", "EnableTweaks", true, "是否应用第三人称手感修正");
            MouseSensX   = c.Bind("Tweaks", "MouseSensitivityX", 2.5f, "鼠标水平灵敏度（游戏原始值 0，UI 范围 0-10）");
            MouseSensY   = c.Bind("Tweaks", "MouseSensitivityY", 2.0f, "鼠标垂直灵敏度（游戏原始值 0，UI 范围 0-10）");
            DirectInput  = c.Bind("Tweaks", "UseDirectInput", true, "视角输入即时响应（原始值 false，走 RotationLagSpeed 平滑）");
            MovementLag  = c.Bind("Tweaks", "MovementLagSpeed", 4.0f, "相机跟随滞后速度，越大跟得越紧（原始值 2.0，UI 范围 0.1-5）");
            CharPosSmooth= c.Bind("Tweaks", "CharacterPositionSmoothSpeed", 20f, "角色位置平滑速度，越大滞后越小（原始值 3.0，UI 范围 0-20）");
            CharRotSmooth= c.Bind("Tweaks", "CharacterRotationSmoothSpeed", 20f, "角色朝向平滑速度（原始值 5.0，UI 范围 0-50）");
            ToggleKey    = c.Bind("Hotkey", "ToggleCameraMode", KeyCode.F9, "切换 俯视/第三人称 的热键");
            FixMovementMapping = c.Bind("Tweaks", "FixMovementMapping", true,
                "第三人称下把移动输入方向基准旋转到相机朝向，使 W 始终朝画面前方。");
            SmoothCameraPosition = c.Bind("Tweaks", "SmoothCameraPosition", true,
                "对第三人称相机位置做平滑，消除撞墙时碰撞命中/未命中交替导致的瞬移抖动（弹簧臂效果）。");
            CamPosSmoothTime = c.Bind("Tweaks", "CameraPositionSmoothTime", 0.15f,
                "相机位置平滑时间（秒），越小跟得越紧，越大弹簧感越强（建议 0.1-0.3）。");

            ClassInjector.RegisterTypeInIl2Cpp<CamWatcher>();
            var go = new GameObject("NRTWThirdPersonCam");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<CamWatcher>();

            // Harmony postfix 挂在 QuantumLocalInputSource.UpdateRInputQuantumInput 上，
            // 由 Il2CppInterop.HarmonySupport 转成对 IL2CPP 原生方法的 detour。
            new Harmony("com.nrtw.thirdpersoncam").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("[NRTW3P] Loaded. F9 toggles TopDown/ThirdPerson camera.");
        }
        catch (Exception e)
        {
            Log.LogError("[NRTW3P] Load failed: " + e);
        }
    }
}

// 移动方向相机相对化修复。
//
// 根因（本轮反汇编实证，详见 06 文档第 2 节）：
//   QuantumLocalInputSource.UpdateRInputQuantumInput（RVA 0x9271350，唯一调用方是 Poll）
//   每帧根据 Rewired 轴直接编码 MovementInputDirection：GetAxis(0/1) → FPVector2(x=横轴,
//   y=纵轴) → InputDirectionMagnitude（1 字节角度 + 1 字节幅度，op_Implicit RVA 0x7211D30）。
//   全程不读 this+0x34（Input.CameraRotation），也不做任何旋转——方向基准恒为世界固定
//   （俯视 yaw=0 设计）。移动模拟侧（GroundMovementData/MovementSystem）原样消费该向量，
//   因此第三人称转动相机后 W 仍走世界北，即用户看到的「视角和输入对不上」。
//
// 修复：postfix 在该函数重建完输入后，把角度字节减去 round(yaw/2)（模 180）。
//   编码格式（decode op_Implicit RVA 0x7211BF0 实证）：
//     角度字节 a → rad = (a-1)*2°，向量 = 幅度 * (-sin(rad), cos(rad))，a∈[1,181]；
//     W（纵轴+1）→ (0,+mag) = 世界 +Z = yaw=0 相机前方。
//   Unity yaw θ 下 W 应为 (sinθ, cosθ) ⇒ rad = -θ ⇒ 新角度字节 = a - round(θ/2)。
//   只动角度字节、不动幅度字节，与游戏自身量化分辨率（2°）一致。
//   UpdateRInputQuantumInput 每次调用都从头重建该字段，postfix 不会叠加旋转；
//   鼠标移动路径（QuantumMouseInputSource.UpdateInput）经实证无任何调用方，不受影响。
[HarmonyPatch(typeof(QuantumLocalInputSource), "UpdateRInputQuantumInput")]
internal static class MovementDirectionPatch
{
    internal static PlayerCamera Cam;
    private static int _nextLogTick;

    private static void Postfix(QuantumLocalInputSource __instance)
    {
        try
        {
            if (!Plugin.FixMovementMapping.Value) return;
            var cam = Cam;
            if (cam == null || cam.Mode != PlayerCameraMode.ThirdPerson) return;
            var t = cam.CameraTransform;
            if (t == null) return;

            float yaw = t.rotation.eulerAngles.y;      // 0..360
            if (yaw > 180f) yaw -= 360f;               // (-180,180]
            int q = (int)System.Math.Round(yaw / 2.0, MidpointRounding.AwayFromZero);
            if (q == 0) return;

            Quantum.Input inp = __instance.m_input;
            InputDirectionMagnitude idm = inp.MovementInputDirection;
            int raw = Unsafe.As<InputDirectionMagnitude, int>(ref idm);
            if (((raw >> 8) & 0xFF) == 0) return;      // 幅度为 0：未移动

            int a = raw & 0xFF;
            int na = (a - 1 - q) % 180;
            if (na < 0) na += 180;
            na += 1;
            raw = (raw & ~0xFF) | na;
            inp.MovementInputDirection = Unsafe.As<int, InputDirectionMagnitude>(ref raw);
            __instance.m_input = inp;

            int now = Environment.TickCount;
            if (now >= _nextLogTick)
            {
                _nextLogTick = now + 2000;
                Plugin.Log.LogInfo($"[NRTW3P] movefix: yaw={yaw:F1} q={q} angleByte {a}->{na}");
            }
        }
        catch (Exception)
        {
            // 输入轮询在模拟关键路径上，任何异常都吞掉，绝不影响游戏
        }
    }
}

public class CamWatcher : MonoBehaviour
{
    private PlayerCamera _cam;
    private float _nextSearch;
    private bool _applied;
    private bool _warnedNoPluginAsset;
    private bool _hasCamera;

    // 相机位置平滑（弹簧臂）状态
    private Vector3 _smoothedCamPos;
    private Vector3 _camPosVel;
    private bool _hasSmoothedPos;
    private int _lastSmoothFrame = -1;

    private Camera.CameraCallback _preCullCallback;

    private void OnEnable()
    {
        // 注意：方法名必须避开 Unity message 名（OnPreCull/OnDisable 等之外的相机回调名），
        // 否则 Unity 会把它当 MonoBehaviour message 每帧尝试调用并报
        // "Script error: OnPreCull — The message may not have any parameters"。
        _preCullCallback = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(SmoothCameraPositionPreCull));
        Camera.onPreCull = Camera.onPreCull == null
            ? _preCullCallback
            : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCullCallback).Cast<Camera.CameraCallback>();
    }

    private void OnDisable()
    {
        if (_preCullCallback != null && Camera.onPreCull != null)
        {
            var remaining = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCullCallback);
            Camera.onPreCull = remaining == null ? null : remaining.Cast<Camera.CameraCallback>();
        }
        _preCullCallback = null;
    }

    // 撞墙抖动修复（弹簧臂）。
    //
    // 根因（反汇编 PlayerCamera.ApplyCollisionConstraints, RVA 0x8B54A90）：
    //  SphereCast 命中时目标距离走 SmoothDamp 平滑收缩；但未命中时直接采用
    //  原始期望位置并同步 m_smoothedCollisionPosition——回弹无平滑。墙角处
    //  命中/未命中逐帧交替，相机在“收缩”和“全臂长”之间瞬移，表现为疯狂闪动。
    // 这里在 onPreCull（游戏已在当帧算完相机位姿、渲染之前）
    //  对 CameraTransform.position 再做一层 SmoothDamp，把瞬移变成快速滑动。
    //  只动位置不动旋转，瞄准/视角响应不受影响。
    private void SmoothCameraPositionPreCull(Camera renderingCam)
    {
        var cam = _cam;
        if (cam == null || !Plugin.SmoothCameraPosition.Value) { _hasSmoothedPos = false; return; }
        try
        {
            if (cam.Mode != PlayerCameraMode.ThirdPerson) { _hasSmoothedPos = false; return; }
            var t = cam.CameraTransform;
            if (t == null) return;
            Vector3 target = t.position;
            if (!_hasSmoothedPos)
            {
                _smoothedCamPos = target; _camPosVel = Vector3.zero;
                _hasSmoothedPos = true; _lastSmoothFrame = Time.frameCount;
                return;
            }
            // onPreCull 每个渲染相机调一次，同帧只推进一次平滑
            if (Time.frameCount != _lastSmoothFrame)
            {
                _lastSmoothFrame = Time.frameCount;
                // 大跨度跳变（传送/切场景，>10m）直接贴合，不做平滑
                if ((_smoothedCamPos - target).sqrMagnitude > 100f)
                {
                    _smoothedCamPos = target; _camPosVel = Vector3.zero;
                }
                else
                {
                    _smoothedCamPos = Vector3.SmoothDamp(_smoothedCamPos, target, ref _camPosVel,
                        Plugin.CamPosSmoothTime.Value, float.PositiveInfinity, Time.deltaTime);
                }
            }
            t.position = _smoothedCamPos;
        }
        catch (Exception)
        {
            _cam = null; _hasSmoothedPos = false;
        }
    }

    private void Update()
    {
        // 热键轮询独立于相机状态，每帧都执行：切世界/相机重建期间按下也要响应，
        // 否则相机查找陷入死循环时（见 FindAliveCamera 注释）F9 永远轮询不到。
        bool toggle = false;
        try { toggle = Input.GetKeyDown(Plugin.ToggleKey.Value); }
        catch (Exception) { }

        if (!IsAlive(_cam))
        {
            if (_hasCamera)
            {
                _hasCamera = false;
                Plugin.Log.LogInfo("[NRTW3P] camera lost (world switch / destroyed), re-searching...");
            }
            _cam = null;
            MovementDirectionPatch.Cam = null;
            _applied = false;
            _hasSmoothedPos = false;

            if (Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 0.5f;
                _cam = FindAliveCamera();
                if (_cam != null)
                {
                    _hasCamera = true;
                    MovementDirectionPatch.Cam = _cam;
                    try { Plugin.Log.LogInfo($"[NRTW3P] camera acquired: {_cam.name} mode={_cam.Mode}"); }
                    catch (Exception) { }
                }
            }
            if (toggle)
                Plugin.Log.LogWarning("[NRTW3P] toggle key pressed but no alive camera yet; ignored");
            return;
        }

        try
        {
            if (Plugin.EnableTweaks.Value) ApplyTweaks();
            if (_cam.Mode == PlayerCameraMode.ThirdPerson) EnsureRotationRangeRelaxed();
            if (toggle) ToggleMode();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[NRTW3P] camera access failed, will re-search: " + e.Message);
            _cam = null; // 相机对象可能已销毁，下帧重新查找
            MovementDirectionPatch.Cam = null;
        }
    }

    // Unity 生命周期检查：已销毁的原生对象在 fake-null 语义下 == null 为 true；
    // 某些状态下 fake-null 失效，再补一次 gameObject 访问（销毁时会抛异常）。
    private static bool IsAlive(PlayerCamera c)
    {
        if (c == null) return false;
        try { return c.gameObject != null; }
        catch (Exception) { return false; }
    }

    // 查找当前存活的玩家相机。
    // 注意：PlayerCamera.All 是游戏维护的静态列表，切世界后可能残留已销毁的旧相机；
    // 不检查存活就会反复抓到同一个死对象，Update 每帧在空分支提前 return，
    // 表现为 F9 失灵、移动输入退回世界坐标（v1.0 的切世界 bug 根因）。
    private static PlayerCamera FindAliveCamera()
    {
        try
        {
            var all = PlayerCamera.All;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (IsAlive(c)) return c;
                }
            }
        }
        catch (Exception) { }

        try
        {
            var found = Resources.FindObjectsOfTypeAll<PlayerCamera>();
            if (found != null)
            {
                foreach (var c in found)
                {
                    if (IsAlive(c) && c.gameObject.activeInHierarchy) return c;
                }
            }
        }
        catch (Exception) { }

        return null;
    }

    private void ApplyTweaks()
    {
        var cfg = _cam.ThirdPersonConfig;
        if (cfg == null) return;
        SetIfDiff(ref cfg, Plugin.MouseSensX.Value, Plugin.MouseSensY.Value);
    }

    // 字段写入集中到一处，便于记录日志（仅首次或数值变化时写）
    private void SetIfDiff(ref ThirdPersonCameraConfig cfg, float msx, float msy)
    {
        bool dirty = false;
        if (Mathf.Abs(cfg.MouseSensitivityX - msx) > 0.0001f) { cfg.MouseSensitivityX = msx; dirty = true; }
        if (Mathf.Abs(cfg.MouseSensitivityY - msy) > 0.0001f) { cfg.MouseSensitivityY = msy; dirty = true; }
        if (cfg.UseDirectInput != Plugin.DirectInput.Value) { cfg.UseDirectInput = Plugin.DirectInput.Value; dirty = true; }
        if (Mathf.Abs(cfg.MovementLagSpeed - Plugin.MovementLag.Value) > 0.0001f) { cfg.MovementLagSpeed = Plugin.MovementLag.Value; dirty = true; }
        if (Mathf.Abs(cfg.CharacterPositionSmoothSpeed - Plugin.CharPosSmooth.Value) > 0.0001f) { cfg.CharacterPositionSmoothSpeed = Plugin.CharPosSmooth.Value; dirty = true; }
        if (Mathf.Abs(cfg.CharacterRotationSmoothSpeed - Plugin.CharRotSmooth.Value) > 0.0001f) { cfg.CharacterRotationSmoothSpeed = Plugin.CharRotSmooth.Value; dirty = true; }
        if (dirty && !_applied)
        {
            Plugin.Log.LogInfo($"[NRTW3P] Tweaks applied: mouse=({msx},{msy}) directInput={Plugin.DirectInput.Value} moveLag={Plugin.MovementLag.Value} charSmooth=({Plugin.CharPosSmooth.Value},{Plugin.CharRotSmooth.Value})");
        }
        _applied = _applied || dirty;
    }

    private void ToggleMode()
    {
        try
        {
            var mode = _cam.Mode;
            var next = mode == PlayerCameraMode.ThirdPerson ? PlayerCameraMode.Gameplay : PlayerCameraMode.ThirdPerson;
            _cam.SetCameraMode(next);
            Plugin.Log.LogInfo("[NRTW3P] Camera mode -> " + next);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[NRTW3P] Toggle failed: " + e.Message);
        }
    }

    // 把 CameraPlayerControllerPlugin 的 Min/MaxValidRotationRange 放宽到第三人称全范围。
    // 正常第三人称 yaw ∈ [-180°, 180°]，pitch 由渲染层钳制到 [-15°, 70°]；这里给足余量，避免
    // 注入模拟层的相机旋转超出资产合法范围时触发 "Injected Camera Rotation is outside the
    // valid range" 异常。
    private void EnsureRotationRangeRelaxed()
    {
        try
        {
            var plugin = GetCameraPlayerControllerPlugin();
            if (plugin == null)
            {
                if (!_warnedNoPluginAsset)
                {
                    Plugin.Log.LogWarning("[NRTW3P] CameraPlayerControllerPlugin asset not resolved yet; rotation range will be relaxed once available.");
                    _warnedNoPluginAsset = true;
                }
                return;
            }

            var min = plugin.MinValidRotationRange;
            var max = plugin.MaxValidRotationRange;
            // 判断是否需要放宽：Y(yaw) 至少覆盖 ±180°，X(pitch)/Z 也放足余量
            bool needMin = (double)min.Y > -181.0 || (double)min.X > -91.0 || (double)min.Z > -91.0;
            bool needMax = (double)max.Y < 181.0 || (double)max.X < 91.0 || (double)max.Z < 91.0;
            if (!needMin && !needMax) return;

            if (needMin)
            {
                min.Y = FP.FromFloat_UNSAFE(-181f);
                min.X = FP.FromFloat_UNSAFE(-91f);
                min.Z = FP.FromFloat_UNSAFE(-91f);
            }
            if (needMax)
            {
                max.Y = FP.FromFloat_UNSAFE(181f);
                max.X = FP.FromFloat_UNSAFE(91f);
                max.Z = FP.FromFloat_UNSAFE(91f);
            }
            plugin.MinValidRotationRange = min;
            plugin.MaxValidRotationRange = max;
            Plugin.Log.LogInfo("[NRTW3P] Relaxed CameraPlayerControllerPlugin rotation range Y -> [-181, 181], X/Z -> [-91, 91]");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[NRTW3P] RelaxRotationRange failed: " + e.Message);
        }
    }

    // 通过 PlayerCamera.PluginAsset 拿到 CameraPlayerControllerPlugin 资产（AssetObject 单例）。
    private CameraPlayerControllerPlugin GetCameraPlayerControllerPlugin()
    {
        var asset = _cam.PluginAsset;
        if (asset == null) return null;

        // 资产解引用需要 IAssetResolutionContext；游戏世界中可直接取 ViewFrame.Active.GetQtnGame().AssetResolver。
        try
        {
            var vf = ViewFrame.Active;
            var game = vf != null ? vf.GetQtnGame() : null;
            var ctx = game != null ? game.AssetResolver : null;
            if (ctx != null)
            {
                var p = asset.Get(ctx);
                if (p != null) return p;
            }
        }
        catch (Exception)
        {
            // 上下文不可用时返回 null，等下一帧重试。
        }

        return null;
    }
}
