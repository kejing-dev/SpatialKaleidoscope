using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Owns the fixed 12-item playlist, the current/incoming scene pair, the 0.6 s crossfade,
    /// pause, the 90 s auto-advance timer, the 20-minute reminder and index persistence.
    /// Resident in the main scene together with the XR Origin and the input router.
    /// </summary>
    public class PlaybackController : MonoBehaviour
    {
        public Camera MainCamera;
        public KaleidoInput Input;
        public BrightnessGovernor Governor;
        public HeadTilt HeadTilt;
        public Transform DomeRoot;
        public Transform ObjectAnchor;
        public GameObject FirstLaunchHint;     // optional; shown 3 s on the very first launch
        public NavButtons Buttons;             // resident previous / next buttons below the horizon
        public bool IsEditorCapture;

        public SceneDefinition[] Playlist { get; private set; }
        public int CurrentIndex { get; private set; }
        public bool Paused { get; private set; }
        public bool Transitioning => _incoming != null;
        public KaleidoSceneBase CurrentScene => _current;
        public KaleidoSceneBase IncomingScene => _incoming;
        public float AutoAdvanceRemaining => Mathf.Max(0f, KaleidoConfig.AutoAdvanceSeconds - _autoTimer);
        public bool ReminderActive => _reminderActive;
        public SceneContext Context => _ctx;

        SceneContext _ctx;
        Mesh _domeMesh;
        KaleidoSceneBase _current, _incoming;
        float _transitionT;
        int _pendingDelta;                 // only the last request during a transition is kept
        float _autoTimer;
        float _startFadeT;
        bool _started;
        bool _anchorPlaced;
        float _anchorWait;
        double _usageSeconds;
        bool _reminderActive;
        bool _suspended;                   // app lost focus
        float _hintTimer = -1f;

        bool _initialised;

        void Awake()
        {
            Initialize();
        }

        /// <summary>Build the context. Called from Awake in play mode, or explicitly by editor tools.</summary>
        public void Initialize()
        {
            if (_initialised) return;
            _initialised = true;
            Playlist = KaleidoPlaylist.Create();
            if (MainCamera == null) MainCamera = Camera.main;
            _domeMesh = MeshUtil.InwardSphere(KaleidoConfig.DomeRadius);
            _ctx = new SceneContext
            {
                DomeRoot = DomeRoot != null ? DomeRoot : transform,
                DomeMesh = _domeMesh,
                ObjectAnchor = ObjectAnchor,
                MainCamera = MainCamera,
                HeadTilt = HeadTilt,
                CellLayer = KaleidoRenderUtil.LayerFor(KaleidoConfig.CellLayerName, 30),
                BulbLayer = KaleidoRenderUtil.LayerFor(KaleidoConfig.BulbLayerName, 31),
                IsEditorCapture = IsEditorCapture,
            };
            CellPhysics.EnsureInitialised();
            EvolutionClock.Reset();
            CurrentIndex = LoadIndex();
        }

        void Start()
        {
            if (!IsEditorCapture) Begin();
        }

        /// <summary>Instantiate the first scene. Safe to call from the editor capture tool as well.</summary>
        public void Begin()
        {
            if (_started) return;
            _started = true;
            if (Governor != null) Governor.ForceImmediate();
            _current = Spawn(CurrentIndex);
            _current.SetIncoming(false);
            _current.ApplyFade(0f, 0f);
            _startFadeT = 0f;
            _autoTimer = 0f;
            if (FirstLaunchHint != null)
            {
                bool first = PlayerPrefs.GetInt(KaleidoConfig.PrefsFirstLaunchKey, 0) == 0;
                FirstLaunchHint.SetActive(first);
                if (first)
                {
                    _hintTimer = KaleidoConfig.FirstLaunchHintSeconds;
                    PlayerPrefs.SetInt(KaleidoConfig.PrefsFirstLaunchKey, 1);
                    PlayerPrefs.Save();
                }
            }
        }

        void Update()
        {
            if (IsEditorCapture) return;
            Step(Time.unscaledDeltaTime, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>One frame of the whole app. The editor capture tool calls this directly.</summary>
        public void Step(float realDt, double realTime)
        {
            if (!_started) return;
            realDt = Mathf.Clamp(realDt, 0f, 0.1f);

            // ----- anchor for 3D objects: fixed once tracking is valid (session world coordinates)
            if (!_anchorPlaced) TryPlaceAnchor(realDt);

            // ----- input
            InputFrame frame = Input != null ? Input.Poll(realTime) : default;
            if (_suspended) frame = default;

            if (frame.AnyValid && _reminderActive)
            {
                // reminder recovery consumes the input (PRD section 7)
                _reminderActive = false;
                _usageSeconds = 0;
                Governor?.SetDimming(1f, 1.0f);
                frame = default;
            }
            // hover feedback: any pointer ray (controller aim, hand aim, head) over a button
            if (Buttons != null)
            {
                int hover = -1;
                if (Input != null)
                {
                    var rays = Input.PointerRays;
                    for (int i = 0; i < rays.Count && hover < 0; i++) hover = Buttons.HitTest(rays[i]);
                }
                Buttons.SetHover(hover);
                Buttons.Step(realDt);
            }

            if (frame.TogglePause) SetPaused(!Paused);
            else if (frame.PreviousKey && _startFadeT >= 1f) RequestStep(-1);
            else if (frame.Click && _startFadeT >= 1f)                         // no paging during the 1 s start fade
            {
                // a click means "next" unless it targets a button (left = previous, right = next)
                int hit = (Buttons != null && frame.HasClickRay) ? Buttons.HitTest(frame.ClickRay) : -1;
                if (hit < 0 && Buttons != null && MainCamera != null)
                    hit = Buttons.HitTest(new Ray(MainCamera.transform.position, MainCamera.transform.forward));
                Buttons?.Flash(hit);
                RequestStep(hit == 0 ? -1 : +1);
            }
            if (frame.AnyValid) _autoTimer = 0f;

            // ----- clocks
            bool simPaused = Paused || _suspended;
            EvolutionClock.SetPaused(simPaused);
            EvolutionClock.Advance(realDt);
            float evoDt = EvolutionClock.DeltaTime;
            HeadTilt?.Step(realDt);
            CellPhysics.Step(evoDt, HeadTilt != null ? HeadTilt.SmoothedRollDeg : 0f);

            // ----- start fade (1 s from black)
            if (_startFadeT < 1f)
            {
                _startFadeT = Mathf.Min(1f, _startFadeT + realDt / KaleidoConfig.StartFadeInSeconds);
                if (_incoming == null) _current.ApplyFade(_startFadeT, _startFadeT);
            }

            // ----- transition
            if (_incoming != null)
            {
                _transitionT = Mathf.Min(1f, _transitionT + realDt / KaleidoConfig.TransitionSeconds);
                float t = Mathf.SmoothStep(0f, 1f, _transitionT);
                _incoming.ApplyFade(t, t);
                _current.ApplyFade(1f, 1f - t);
                if (_transitionT >= 1f) FinishTransition();
            }
            else if (!Paused && !_suspended)
            {
                _autoTimer += realDt;
                if (_autoTimer >= KaleidoConfig.AutoAdvanceSeconds)
                {
                    _autoTimer = 0f;
                    StartTransition(+1);
                }
            }

            // ----- tick scenes (dt = 0 while paused so every evolution freezes)
            _current?.Tick(evoDt, EvolutionClock.Time);
            _incoming?.Tick(evoDt, EvolutionClock.Time);

            // ----- 20-minute reminder
            if (!_suspended)
            {
                _usageSeconds += realDt;
                if (!_reminderActive && _usageSeconds >= KaleidoConfig.ReminderMinutes * 60.0)
                {
                    _reminderActive = true;
                    Governor?.SetDimming(KaleidoConfig.ReminderDimTarget, KaleidoConfig.ReminderDimSeconds);
                }
            }
            Governor?.Step(realDt);

            // ----- first-launch hint
            if (_hintTimer > 0f)
            {
                _hintTimer -= realDt;
                if (_hintTimer <= 0f && FirstLaunchHint != null) FirstLaunchHint.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ commands
        public void SetPaused(bool paused)
        {
            Paused = paused;
            EvolutionClock.SetPaused(paused || _suspended);
            if (!paused) _autoTimer = 0f;   // resume restarts the 90 s timer
        }

        public void RequestStep(int delta)
        {
            if (_incoming != null)
            {
                _pendingDelta = delta;      // only the last request survives; executed after the transition
                return;
            }
            StartTransition(delta);
        }

        void StartTransition(int delta)
        {
            int next = ((CurrentIndex + delta) % Playlist.Length + Playlist.Length) % Playlist.Length;
            _incoming = Spawn(next);
            _incoming.SetIncoming(true);
            _incoming.ApplyFade(0f, 0f);
            _transitionT = 0f;
            _autoTimer = 0f;
            CurrentIndex = next;
            PlayerPrefs.SetInt(KaleidoConfig.PrefsIndexKey, CurrentIndex);   // flushed on suspend / quit (no disk hit per page)
        }

        void FinishTransition()
        {
            var old = _current;
            _current = _incoming;
            _incoming = null;
            _current.SetIncoming(false);
            _current.ApplyFade(1f, 1f);
            old.Teardown();
            if (_pendingDelta != 0)
            {
                int d = _pendingDelta;
                _pendingDelta = 0;
                StartTransition(d);
            }
        }

        /// <summary>Editor/capture helper: replace the current scene immediately, no crossfade.</summary>
        public void JumpTo(int index)
        {
            _incoming?.Teardown();
            _incoming = null;
            _pendingDelta = 0;
            _current?.Teardown();
            CurrentIndex = ((index % Playlist.Length) + Playlist.Length) % Playlist.Length;
            _current = Spawn(CurrentIndex);
            _current.SetIncoming(false);
            _current.ApplyFade(1f, 1f);
            _startFadeT = 1f;
            _autoTimer = 0f;
        }

        KaleidoSceneBase Spawn(int index)
        {
            var def = Playlist[index];
            var go = new GameObject($"Scene{index + 1:00}_{def.Id}");
            go.transform.SetParent(transform, false);
            var scene = def.Factory(go);
            scene.Setup(_ctx, def, index);
            return scene;
        }

        // ------------------------------------------------------------------ anchor
        void TryPlaceAnchor(float dt)
        {
            if (ObjectAnchor == null || MainCamera == null) { _anchorPlaced = true; return; }
            _anchorWait += dt;
            var cam = MainCamera.transform;
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.CenterEye);
            bool tracked = head.isValid && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool t) && t
                           && cam.localPosition.sqrMagnitude > 1e-4f;
            if (!tracked && _anchorWait < 1.0f) return; // give tracking a moment before falling back (hidden by the start fade)
            Vector3 fwd = cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            fwd.Normalize();
            float eyeHeight = cam.position.y > 0.3f ? cam.position.y : 1.6f;
            ObjectAnchor.position = new Vector3(cam.position.x, eyeHeight, cam.position.z) + fwd * KaleidoConfig.ObjectDistance;
            ObjectAnchor.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            Buttons?.Place(new Vector3(cam.position.x, eyeHeight, cam.position.z), fwd);
            if (FirstLaunchHint != null)
            {
                FirstLaunchHint.transform.SetPositionAndRotation(
                    new Vector3(cam.position.x, eyeHeight - 0.15f, cam.position.z) + fwd * 1.6f,
                    Quaternion.LookRotation(fwd, Vector3.up));
            }
            _anchorPlaced = true;
        }

        // ------------------------------------------------------------------ persistence & lifecycle
        static int LoadIndex()
        {
            int i = PlayerPrefs.GetInt(KaleidoConfig.PrefsIndexKey, 0);
            if (i < 0 || i >= KaleidoConfig.SceneCount) i = 0;   // bad value -> scene 1
            return i;
        }

        void SaveIndex()
        {
            PlayerPrefs.SetInt(KaleidoConfig.PrefsIndexKey, CurrentIndex);
            PlayerPrefs.Save();
        }

        void OnApplicationQuit() => SaveIndex();
        void OnApplicationPause(bool paused) => SetSuspended(paused);
        void OnApplicationFocus(bool focus) => SetSuspended(!focus);

        void SetSuspended(bool suspended)
        {
            if (IsEditorCapture) return;
            if (_suspended == suspended) return;
            _suspended = suspended;
            EvolutionClock.SetPaused(Paused || _suspended);
            if (suspended) SaveIndex();
            else Input?.ClearResidual();
        }

        void OnDestroy()
        {
            _incoming?.Teardown();
            _current?.Teardown();
            KaleidoRenderUtil.SafeDestroy(_domeMesh);
        }
    }
}
