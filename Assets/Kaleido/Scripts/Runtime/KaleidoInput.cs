using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Kaleido
{
    /// <summary>Per-frame arbitrated command set.</summary>
    public struct InputFrame
    {
        /// <summary>A single "select" happened this frame (short pinch release, trigger / A / X press, mouse click).</summary>
        public bool Click;
        /// <summary>Ray of the source that clicked (controller aim, hand aim, or head), if any.</summary>
        public bool HasClickRay;
        public Ray ClickRay;
        /// <summary>Dev keyboard: explicit previous (left arrow).</summary>
        public bool PreviousKey;
        public bool TogglePause;
        public bool AnyValid => Click || PreviousKey || TogglePause;
    }

    /// <summary>
    /// Unified input layer. Sources:
    ///  - eye-hand: the system hand-interaction pinch (PicoHandInteraction Input System device,
    ///    pinchValue / pinchReady / pointer pose), falling back to the legacy aim state
    ///  - VR motion controllers: trigger or A/X = click, any grip = pause
    ///  - keyboard / mouse (development only)
    /// Rules: every press / short pinch fires exactly once; long pinch (>= 1 s) toggles pause once and
    /// its release does nothing; pause wins in a frame; tracking loss cancels an in-progress pinch;
    /// an input-source change clears residual state. What a click means (next, or the button it
    /// points at) is decided by the PlaybackController using ClickRay.
    /// </summary>
    public class KaleidoInput : MonoBehaviour
    {
        public bool EnableKeyboardMouse = true;
        public Camera Head;                       // for head-ray fallback
        public Transform TrackingSpace;           // XR Origin's camera offset: device poses are local to it

        // controller edge state (combined per side)
        bool _rightHeld, _leftHeld, _gripHeld;

        // hand pinch state
        struct PinchState { public bool Pinching; public double Start; public bool LongFired; public bool Valid; public bool AwaitRelease; }
        static readonly PinchState Cancelled = new PinchState { AwaitRelease = true };
        PinchState _rightPinch, _leftPinch;
        int _lastActiveInputDevice = -1;

        public InputFrame Current { get; private set; }
        public string LastSource { get; private set; } = "none";
        public string HandStatus { get; private set; } = "n/a";

        readonly List<Ray> _pointerRays = new List<Ray>(4);
        /// <summary>Rays that currently "point" (controller aim, tracked hand aim, head). Used for hover.</summary>
        public IReadOnlyList<Ray> PointerRays => _pointerRays;

        public void ClearResidual()
        {
            _rightPinch = Cancelled;
            _leftPinch = Cancelled;
            ReadControllers(out _rightHeld, out _leftHeld, out _gripHeld);
            Current = default;
        }

        /// <summary>Poll all sources; call exactly once per frame.</summary>
        public InputFrame Poll(double realTime)
        {
            var f = new InputFrame();
            bool pause = false;
            string src = null;
            _pointerRays.Clear();

            // ---------------- controllers
            ReadControllers(out bool rightNow, out bool leftNow, out bool gripNow);
            if (rightNow && !_rightHeld) { Click(ref f, ControllerRay(true), "controller-right"); src = "controller-right"; }
            if (leftNow && !_leftHeld) { Click(ref f, ControllerRay(false), "controller-left"); src = "controller-left"; }
            if (gripNow && !_gripHeld) { pause = true; src = "controller-grip"; }
            _rightHeld = rightNow; _leftHeld = leftNow; _gripHeld = gripNow;
            AddPointer(ControllerRay(true));
            AddPointer(ControllerRay(false));

            // ---------------- eye-hand (system pinch)
            int active = ActiveInputDeviceId();
            if (active != _lastActiveInputDevice)
            {
                _lastActiveInputDevice = active;
                _rightPinch = Cancelled;
                _leftPinch = Cancelled;
            }
            bool controllersActive = active == 1;   // ActiveInputDevice.ControllerActive: ignore stray pinches
            if (controllersActive)
            {
                _rightPinch = Cancelled;
                _leftPinch = Cancelled;
            }
            else
            {
                if (StepPinch(ref _rightPinch, true, realTime, out bool rShort, out bool rLong))
                {
                    if (rShort) { Click(ref f, HandRay(true), "hand-right"); src = "hand-right"; }
                    if (rLong) { pause = true; src = "hand-right-long"; }
                }
                if (StepPinch(ref _leftPinch, false, realTime, out bool lShort, out bool lLong))
                {
                    if (lShort) { Click(ref f, HandRay(false), "hand-left"); src = "hand-left"; }
                    if (lLong) { pause = true; src = "hand-left-long"; }
                }
                AddPointer(HandRay(true));
                AddPointer(HandRay(false));
            }

            // ---------------- keyboard / mouse (development only)
#if UNITY_EDITOR
            EnableKeyboardMouse = true;
#endif
            if (EnableKeyboardMouse)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.rightArrowKey.wasPressedThisFrame) { Click(ref f, HeadRay(), "keyboard"); src = "keyboard"; }
                    if (kb.leftArrowKey.wasPressedThisFrame) { f.PreviousKey = true; src = "keyboard"; }
                    if (kb.spaceKey.wasPressedThisFrame) { pause = true; src = "keyboard"; }
                }
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Click(ref f, HeadRay(), "mouse"); src = "mouse"; }
            }

            // head ray is always a pointer (hover feedback when looking at a button)
            AddPointer(HeadRay());

            // ---------------- arbitration: pause wins
            if (pause) { f = new InputFrame { TogglePause = true }; }
            if (src != null) LastSource = src;
            Current = f;
            return f;
        }

        static void Click(ref InputFrame f, Ray? ray, string source)
        {
            f.Click = true;
            if (ray.HasValue) { f.HasClickRay = true; f.ClickRay = ray.Value; }
        }

        void AddPointer(Ray? r) { if (r.HasValue) _pointerRays.Add(r.Value); }

        Ray? HeadRay()
        {
            if (Head == null) return null;
            return new Ray(Head.transform.position, Head.transform.forward);
        }

        Ray? LocalPoseRay(Vector3 localPos, Quaternion localRot)
        {
            if (TrackingSpace != null)
                return new Ray(TrackingSpace.TransformPoint(localPos), TrackingSpace.TransformDirection(localRot * Vector3.forward));
            return new Ray(localPos, localRot * Vector3.forward);
        }

        // ------------------------------------------------------------------ controllers
        static void ReadControllers(out bool right, out bool left, out bool grip)
        {
            right = SidePressed(XRController.rightHand, UnityEngine.XR.XRNode.RightHand);
            left = SidePressed(XRController.leftHand, UnityEngine.XR.XRNode.LeftHand);
            grip = GripPressed(XRController.rightHand, UnityEngine.XR.XRNode.RightHand) ||
                   GripPressed(XRController.leftHand, UnityEngine.XR.XRNode.LeftHand);
        }

        Ray? ControllerRay(bool right)
        {
            var ctrl = right ? XRController.rightHand : XRController.leftHand;
            if (ctrl == null) return null;
            var tracked = ctrl.TryGetChildControl<ButtonControl>("isTracked");
            if (tracked != null && !tracked.isPressed) return null;
            // prefer an explicit pointer / aim pose when the layout has one
            var pp = ctrl.TryGetChildControl<Vector3Control>("pointerPosition");
            var pr = ctrl.TryGetChildControl<QuaternionControl>("pointerRotation");
            if (pp != null && pr != null) return LocalPoseRay(pp.ReadValue(), pr.ReadValue());
            return LocalPoseRay(ctrl.devicePosition.ReadValue(), ctrl.deviceRotation.ReadValue());
        }

        static bool SidePressed(XRController ctrl, UnityEngine.XR.XRNode node)
        {
            bool pressed = false;
            if (ctrl != null)
            {
                pressed |= Button(ctrl, "triggerPressed") || Button(ctrl, "triggerButton");
                pressed |= Button(ctrl, "primaryButton");
            }
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid)
            {
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool t) && t) pressed = true;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool p) && p) pressed = true;
            }
            return pressed;
        }

        static bool GripPressed(XRController ctrl, UnityEngine.XR.XRNode node)
        {
            bool pressed = false;
            if (ctrl != null) pressed |= Button(ctrl, "gripPressed") || Button(ctrl, "gripButton");
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid && dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g) && g) pressed = true;
            return pressed;
        }

        static bool Button(InputDevice dev, string controlName)
        {
            var c = dev.TryGetChildControl<ButtonControl>(controlName);
            return c != null && c.isPressed;
        }

        // ------------------------------------------------------------------ hands
        static int ActiveInputDeviceId()
        {
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            try { return (int)PXR_HandTracking.GetActiveInputDevice(); } catch { return -1; }
#else
            return -1;
#endif
        }

        /// <summary>The PicoHandInteraction Input System device for a side (created by the PICO hand subsystem), or null.</summary>
        static InputDevice HandDevice(bool right)
        {
            var devices = InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d.layout != "PicoHandInteraction") continue;
                var usages = d.usages;
                bool isRight = false, isLeft = false;
                for (int u = 0; u < usages.Count; u++)
                {
                    if (usages[u] == UnityEngine.InputSystem.CommonUsages.RightHand) isRight = true;
                    if (usages[u] == UnityEngine.InputSystem.CommonUsages.LeftHand) isLeft = true;
                }
                if (right && isRight) return d;
                if (!right && isLeft) return d;
            }
            return null;
        }

        Ray? HandRay(bool right)
        {
            var dev = HandDevice(right);
            if (dev == null) return null;
            var tracked = dev.TryGetChildControl<ButtonControl>("isTracked");
            if (tracked != null && !tracked.isPressed) return null;
            var pp = dev.TryGetChildControl<Vector3Control>("pointerPosition");
            var pr = dev.TryGetChildControl<QuaternionControl>("pointerRotation");
            if (pp != null && pr != null) return LocalPoseRay(pp.ReadValue(), pr.ReadValue());
            return null;
        }

        /// <summary>Returns true when an event is produced (short = on release, long = at 1 s).</summary>
        bool StepPinch(ref PinchState s, bool rightHand, double now, out bool shortEvent, out bool longEvent)
        {
            shortEvent = false; longEvent = false;
            ReadPinch(rightHand, out bool computed, out bool pinching);

            if (!computed)
            {
                s = Cancelled;
                return false;
            }
            if (s.AwaitRelease)
            {
                if (!pinching) s = default;
                return false;
            }
            if (pinching && !s.Pinching)
            {
                s.Pinching = true; s.Start = now; s.LongFired = false; s.Valid = true;
                return false;
            }
            if (pinching && s.Pinching)
            {
                if (!s.LongFired && now - s.Start >= KaleidoConfig.LongPinchSeconds)
                {
                    s.LongFired = true;
                    longEvent = true;
                    return true;
                }
                return false;
            }
            if (!pinching && s.Pinching)
            {
                bool wasShort = !s.LongFired && s.Valid && (now - s.Start) < KaleidoConfig.LongPinchSeconds;
                s = default;
                if (wasShort) { shortEvent = true; return true; }
                return false;
            }
            return false;
        }

        // hysteresis on the system pinch value (PicoHandInteraction.pressThreshold is 0.8)
        bool _rightPinchLatched, _leftPinchLatched;

        void ReadPinch(bool rightHand, out bool computed, out bool pinching)
        {
            computed = false; pinching = false;
            ref bool latched = ref (rightHand ? ref _rightPinchLatched : ref _leftPinchLatched);

            // 1) system hand interaction device (XR_EXT_hand_interaction style): pinchValue + pinchReady
            var dev = HandDevice(rightHand);
            if (dev != null)
            {
                var tracked = dev.TryGetChildControl<ButtonControl>("isTracked");
                var value = dev.TryGetChildControl<AxisControl>("pinchValue");
                if (tracked != null && value != null)
                {
                    computed = tracked.isPressed;
                    float v = value.ReadValue();
                    if (computed)
                    {
                        if (!latched && v > 0.8f) latched = true;
                        else if (latched && v < 0.5f) latched = false;
                    }
                    else latched = false;
                    pinching = computed && latched;
                    HandStatus = (rightHand ? "R" : "L") + " dev tracked=" + computed + " pinch=" + v.ToString("F2");
                    return;
                }
            }

            // 2) legacy aim state (older runtimes): the SDK's own PXR_Hand treats AimRayTouched as "pinch"
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            try
            {
                var state = new HandAimState();
                if (PXR_HandTracking.GetAimState(rightHand ? HandType.HandRight : HandType.HandLeft, ref state))
                {
                    computed = (state.aimStatus & HandAimStatus.AimComputed) != 0;
                    bool touched = (state.aimStatus & HandAimStatus.AimRayTouched) != 0;
                    bool index = (state.aimStatus & HandAimStatus.AimIndexPinching) != 0;
                    pinching = computed && (touched || index);
                    HandStatus = (rightHand ? "R" : "L") + " aim computed=" + computed + " touched=" + touched + " index=" + index;
                }
            }
            catch { computed = false; pinching = false; }
#endif
            latched = pinching;
        }
    }
}
