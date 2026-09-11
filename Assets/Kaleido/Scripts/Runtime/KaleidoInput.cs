using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Kaleido
{
    /// <summary>Per-frame arbitrated command set (PRD section 3).</summary>
    public struct InputFrame
    {
        public bool Next;
        public bool Previous;
        public bool TogglePause;
        public bool AnyValid => Next || Previous || TogglePause;
    }

    /// <summary>
    /// Unified input layer: eye-hand system pinch (PICO hand aim state), VR motion controllers and
    /// keyboard/mouse (development only). Outputs Next / Previous / TogglePause once per frame with
    /// the PRD arbitration rules:
    ///  - every controller press, short pinch or long pinch fires exactly once
    ///  - same-side trigger and A/X are merged into one press (edge on the OR of both)
    ///  - pause wins in a frame; simultaneous left+right paging cancels out
    ///  - short pinch fires on release (&lt; 1 s); long pinch (>= 1 s) toggles pause once, release does nothing
    ///  - tracking loss or input-source change cancels an in-progress pinch and clears residual edges
    /// </summary>
    public class KaleidoInput : MonoBehaviour
    {
        public bool EnableKeyboardMouse = true;

        // controller edge state (combined per side)
        bool _rightHeld, _leftHeld, _gripHeld;

        // hand pinch state
        struct PinchState { public bool Pinching; public double Start; public bool LongFired; public bool Valid; public bool AwaitRelease; }
        static readonly PinchState Cancelled = new PinchState { AwaitRelease = true };
        PinchState _rightPinch, _leftPinch;
        int _lastActiveInputDevice = -1;

        public InputFrame Current { get; private set; }
        public string LastSource { get; private set; } = "none";

        /// <summary>Forget any half-finished gesture and re-sync edge detectors (focus regained, source changed).</summary>
        public void ClearResidual()
        {
            _rightPinch = Cancelled;   // a pinch that is still closed must open before it can count again
            _leftPinch = Cancelled;
            ReadControllers(out _rightHeld, out _leftHeld, out _gripHeld);
            Current = default;
        }

        /// <summary>Poll all sources; call exactly once per frame.</summary>
        public InputFrame Poll(double realTime)
        {
            bool next = false, prev = false, pause = false;
            string src = null;

            // ---------------- controllers
            ReadControllers(out bool rightNow, out bool leftNow, out bool gripNow);
            if (rightNow && !_rightHeld) { next = true; src = "controller-right"; }
            if (leftNow && !_leftHeld) { prev = true; src = "controller-left"; }
            if (gripNow && !_gripHeld) { pause = true; src = "controller-grip"; }
            _rightHeld = rightNow; _leftHeld = leftNow; _gripHeld = gripNow;

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
                    if (rShort) { next = true; src = "hand-right"; }
                    if (rLong) { pause = true; src = "hand-right-long"; }
                }
                if (StepPinch(ref _leftPinch, false, realTime, out bool lShort, out bool lLong))
                {
                    if (lShort) { prev = true; src = "hand-left"; }
                    if (lLong) { pause = true; src = "hand-left-long"; }
                }
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
                    if (kb.rightArrowKey.wasPressedThisFrame) { next = true; src = "keyboard"; }
                    if (kb.leftArrowKey.wasPressedThisFrame) { prev = true; src = "keyboard"; }
                    if (kb.spaceKey.wasPressedThisFrame) { pause = true; src = "keyboard"; }
                }
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) { next = true; src = "mouse"; }
            }

            // ---------------- arbitration
            var f = new InputFrame();
            if (pause) { f.TogglePause = true; }
            else if (next && prev) { /* simultaneous opposite paging: no change */ }
            else { f.Next = next; f.Previous = prev; }
            if (src != null) LastSource = src;
            Current = f;
            return f;
        }

        // ------------------------------------------------------------------ controllers
        static void ReadControllers(out bool right, out bool left, out bool grip)
        {
            right = SidePressed(XRController.rightHand, UnityEngine.XR.XRNode.RightHand);
            left = SidePressed(XRController.leftHand, UnityEngine.XR.XRNode.LeftHand);
            grip = GripPressed(XRController.rightHand, UnityEngine.XR.XRNode.RightHand) ||
                   GripPressed(XRController.leftHand, UnityEngine.XR.XRNode.LeftHand);
        }

        static bool SidePressed(XRController ctrl, UnityEngine.XR.XRNode node)
        {
            bool pressed = false;
            if (ctrl != null)
            {
                pressed |= Button(ctrl, "triggerPressed") || Button(ctrl, "triggerButton");
                pressed |= Button(ctrl, "primaryButton");
            }
            // legacy XR input path (works for any controller layout)
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

        /// <summary>Returns true when an event is produced (short = on release, long = at 1 s).</summary>
        static bool StepPinch(ref PinchState s, bool rightHand, double now, out bool shortEvent, out bool longEvent)
        {
            shortEvent = false; longEvent = false;
            bool computed, pinching;
            ReadPinch(rightHand, out computed, out pinching);

            if (!computed)
            {
                // tracking lost: cancel anything in progress, no event; the hand must open before a new pinch counts
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

        static void ReadPinch(bool rightHand, out bool computed, out bool pinching)
        {
            computed = false; pinching = false;
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            try
            {
                var state = new HandAimState();
                if (PXR_HandTracking.GetAimState(rightHand ? HandType.HandRight : HandType.HandLeft, ref state))
                {
                    computed = (state.aimStatus & HandAimStatus.AimComputed) != 0;
                    pinching = computed && (state.aimStatus & HandAimStatus.AimIndexPinching) != 0;
                }
            }
            catch { computed = false; pinching = false; }
#endif
        }
    }
}
