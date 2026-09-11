using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Development-only command channel used by the automated emulator verification loop
    /// (compiled only with the KALEIDO_DEBUG define or in the editor). Plain-text TCP on
    /// port 7777 (adb forward tcp:7777 tcp:7777). One command per line:
    ///   next | prev | pause | goto N | look YAW PITCH | status | brightness X
    /// "look" rotates the XR rig so screenshots can be taken from several head orientations.
    /// </summary>
    public class KaleidoDebugServer : MonoBehaviour
    {
        public PlaybackController Controller;
        public Transform RigRoot;                  // XR Origin transform rotated by "look"
        public int Port = 7777;

        TcpListener _listener;
        Thread _thread;
        volatile bool _running;
        readonly ConcurrentQueue<(string cmd, TcpClient client)> _queue = new ConcurrentQueue<(string, TcpClient)>();

        void OnEnable()
        {
#if !(KALEIDO_DEBUG || UNITY_EDITOR)
            enabled = false;   // never active in release players, whatever the scene says
            return;
#endif
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);   // adb forward only
                _listener.Start();
                _running = true;
                _thread = new Thread(AcceptLoop) { IsBackground = true };
                _thread.Start();
                Debug.Log("[KaleidoDebug] listening on " + Port);
            }
            catch (Exception e) { Debug.LogWarning("[KaleidoDebug] cannot listen: " + e.Message); }
        }

        void OnDisable()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    client.ReceiveTimeout = 2000;
                    var stream = client.GetStream();
                    var buf = new byte[512];
                    int n = stream.Read(buf, 0, buf.Length);
                    string cmd = Encoding.UTF8.GetString(buf, 0, Math.Max(n, 0)).Trim();
                    _queue.Enqueue((cmd, client));
                }
                catch { if (!_running) break; Thread.Sleep(100); }
            }
        }

        int _frame;

        void Update()
        {
            _frame++;
            while (_queue.TryDequeue(out var item))
            {
                string reply;
                try { reply = Execute(item.cmd); }
                catch (Exception e) { reply = "error " + e.Message; }
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                    item.client.GetStream().Write(bytes, 0, bytes.Length);
                    item.client.Close();
                }
                catch { }
            }
        }

        string Execute(string line)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "empty";
            switch (parts[0].ToLowerInvariant())
            {
                case "next": Controller.RequestStep(+1); return "ok next -> " + (Controller.CurrentIndex + 1);
                case "prev": Controller.RequestStep(-1); return "ok prev -> " + (Controller.CurrentIndex + 1);
                case "pause": Controller.SetPaused(!Controller.Paused); return "ok paused=" + Controller.Paused;
                case "goto":
                    {
                        int i = int.Parse(parts[1]) - 1;
                        Controller.JumpTo(i);
                        return "ok goto " + (i + 1);
                    }
                case "look":
                    {
                        float yaw = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                        float pitch = parts.Length > 2 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                        if (RigRoot != null) RigRoot.rotation = Quaternion.Euler(pitch, yaw, 0f);
                        return "ok look " + yaw + " " + pitch;
                    }
                case "move":
                    {
                        float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                        float y = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                        float z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                        if (RigRoot != null) RigRoot.position = new Vector3(x, y, z);
                        return "ok move";
                    }
                case "warp":
                    {
                        // advance evolution quickly without rendering (for slow emulators / long cycles)
                        float seconds = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                        int steps = Mathf.CeilToInt(seconds / 0.05f);
                        double t = Time.realtimeSinceStartupAsDouble;
                        for (int i = 0; i < steps; i++) Controller.Step(0.05f, t + i * 0.05);
                        return "ok warp " + seconds;
                    }
                case "frame": return "frame=" + _frame;
                case "click":
                    {
                        // simulate a select along the current head ray (resolves buttons like a real click)
                        var cam = Controller.MainCamera;
                        int hit = (Controller.Buttons != null && cam != null) ? Controller.Buttons.HitTest(new Ray(cam.transform.position, cam.transform.forward)) : -1;
                        Controller.Buttons?.Flash(hit);
                        Controller.RequestStep(hit == 0 ? -1 : +1);
                        return "ok click hit=" + hit + " -> " + (Controller.CurrentIndex + 1);
                    }
                case "hand": return "hand: " + (Controller.Input != null ? Controller.Input.HandStatus : "n/a");
                case "status":
                    {
                        var cam = Controller.MainCamera != null ? Controller.MainCamera.transform : null;
                        return $"frame={_frame} scene={Controller.CurrentIndex + 1} name={Controller.CurrentScene?.DisplayName} paused={Controller.Paused} transitioning={Controller.Transitioning} " +
                               $"auto={Controller.AutoAdvanceRemaining:F1} fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):F1} " +
                               $"cam={(cam != null ? cam.position.ToString("F2") + " rot=" + cam.eulerAngles.ToString("F0") : "none")} " +
                               $"xr={UnityEngine.XR.XRSettings.enabled} eye={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} " +
                               $"lastInput={Controller.Input?.LastSource}";
                    }
                default: return "unknown " + parts[0];
            }
        }
    }
}
