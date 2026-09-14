using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Double-buffered, frame-sliced render-to-texture pipeline shared by the fractal dome scenes
    /// (Julia, Mandelbrot deep zoom).
    ///
    /// Two square RenderTextures: <see cref="Front"/> is the finished image the dome shows,
    /// <see cref="Back"/> is being filled in horizontal bands. Every <see cref="Step"/> renders ONE
    /// band of the back buffer with a full-screen Graphics.Blit through the compute material
    /// (the shader discards every texel outside band _Band.x of _Band.y, so a band costs
    /// size*size/bands shaded texels plus a trivial discard for the rest). When the last band has
    /// been rendered the buffers swap and Step returns true; the caller then re-points its dome
    /// material at <see cref="Front"/>.
    ///
    /// Contract:
    ///  - the caller sets ALL fractal uniforms on the material BEFORE <see cref="Begin"/> and must
    ///    not touch them until the image completes (all bands of one image share one parameter set,
    ///    so a half-old / half-new image, i.e. a visible seam, can never happen);
    ///  - the compute shader declares float4 _Band (default (0,1,0,0)) and starts its fragment
    ///    with <c>if (floor(uv.y * _Band.y) != _Band.x) discard;</c>;
    ///  - the caller owns the two textures (wrap <see cref="Front"/> and <see cref="Back"/> in
    ///    KaleidoSceneBase.Own right after construction); this class never destroys them;
    ///  - no Unity callbacks are used, so it works in play mode and under the editor capture tool.
    ///
    /// Graphics.Blit never clears the target, so the texels of the bands not rendered this Step keep
    /// their previous content until their own band pass overwrites them; because nobody samples the
    /// back buffer before the swap, the dome only ever sees complete images.
    /// </summary>
    public sealed class FractalTexturePipeline
    {
        // Matches the Properties blocks of Kaleido/ComputeJulia and Kaleido/ComputeMandelbrot exactly.
        static readonly int BandId = Shader.PropertyToID("_Band");

        readonly int _bandCount;
        RenderTexture _front;
        RenderTexture _back;
        Material _material;
        int _nextBand = -1;     // band rendered by the next Step(); -1 = no image in progress

        /// <summary>Finished image (what the dome should sample). Changes identity on every swap.</summary>
        public RenderTexture Front => _front;
        /// <summary>Image under construction. Never sample it.</summary>
        public RenderTexture Back => _back;
        /// <summary>Square texture size in texels.</summary>
        public int Size { get; }
        /// <summary>Number of Step() calls one image takes.</summary>
        public int BandCount => _bandCount;
        /// <summary>True between Begin() and the Step() that completed the image.</summary>
        public bool InProgress => _nextBand >= 0;

        /// <summary>
        /// Creates both textures through <see cref="KaleidoRenderUtil.CreateRT"/>, colour only
        /// (depthBits 0): they are Graphics.Blit targets drawn with ZWrite Off / ZTest Always and
        /// never URP camera targets (the depth buffer CreateRT adds by default is only needed for
        /// render-graph camera targets), so a depth surface would just cost memory (2 x 16 MB at
        /// 2048^2) plus a full depth load/store per band on the tiled GPU.
        /// Mipmapped textures are sampled trilinearly: the dome minifies a 2048 texture below the
        /// horizon, and Unity's Bilinear mode picks the NEAREST mip, which would snap from mip 0 to
        /// mip 1 along a circle around the viewer - a sharpness ring that moves with the head.
        /// The caller must Own() Front and Back.
        /// </summary>
        public FractalTexturePipeline(int size, RenderTextureFormat format, bool mips, int bandsPerImage, string name)
        {
            Size = Mathf.Max(1, size);
            _bandCount = Mathf.Max(1, bandsPerImage);
            _front = KaleidoRenderUtil.CreateRT(Size, Size, mips, name + "_A", format, depthBits: 0);
            _back = KaleidoRenderUtil.CreateRT(Size, Size, mips, name + "_B", format, depthBits: 0);
            if (mips)
            {
                // sampler state, settable after Create() (same as CellRT in PhysicalKaleidoscopeScene)
                _front.filterMode = FilterMode.Trilinear;
                _back.filterMode = FilterMode.Trilinear;
            }
        }

        /// <summary>
        /// Start a new image with <paramref name="mat"/> (all fractal uniforms already set on it).
        /// Restarts from band 0 if an image was in progress.
        /// </summary>
        public void Begin(Material mat)
        {
            _material = mat;
            _nextBand = mat != null ? 0 : -1;
        }

        /// <summary>
        /// Render the next band of the back buffer. Returns true when that was the last band of the
        /// image: the buffers have been swapped and <see cref="Front"/> now holds the new image.
        /// Does nothing (returns false) when no image is in progress.
        /// </summary>
        public bool Step()
        {
            if (_nextBand < 0) return false;
            if (_material == null || _back == null) { _nextBand = -1; return false; }

            _material.SetVector(BandId, new Vector4(_nextBand, _bandCount, 0f, 0f));

            // Graphics.Blit changes RenderTexture.active; restore it so callers (the editor capture
            // tool reads pixels in the same call chain) are not disturbed.
            var prevActive = RenderTexture.active;
            Graphics.Blit(null, _back, _material, 0);
            RenderTexture.active = prevActive;

            _nextBand++;
            if (_nextBand < _bandCount) return false;

            _nextBand = -1;
            var finished = _back;
            _back = _front;
            _front = finished;
            return true;
        }

        /// <summary>
        /// Render every band of a new image right now and swap (setup, and recovery after the
        /// render textures were lost). Leaves no image in progress.
        /// </summary>
        public void RenderAll(Material mat)
        {
            Begin(mat);
            // bounded loop (never spins if Step bails out on a destroyed texture / null material)
            for (int i = 0; i < _bandCount; i++)
            {
                if (Step()) break;
            }
            _nextBand = -1;
        }

        /// <summary>
        /// Re-create any texture whose GPU resource was lost (e.g. Android app suspend). Returns true
        /// when something had to be re-created: the contents are gone, so the caller must RenderAll.
        /// </summary>
        public bool EnsureCreated()
        {
            bool lost = false;
            if (_front != null && !_front.IsCreated()) { _front.Create(); lost = true; }
            if (_back != null && !_back.IsCreated()) { _back.Create(); lost = true; }
            return lost;
        }
    }
}
