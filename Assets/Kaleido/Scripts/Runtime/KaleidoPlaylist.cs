using UnityEngine;
using Kaleido.Scenes;

namespace Kaleido
{
    /// <summary>The fixed content registry (device review: the two three-mirror scopes were dropped). Order is part of the product.</summary>
    public static class KaleidoPlaylist
    {
        public static SceneDefinition[] Create()
        {
            return new[]
            {
                new SceneDefinition("beads2", "1 两镜暗场发光珠", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.TwoMirrorDarkBeads()),
                new SceneDefinition("glass2", "2 两镜六重玻璃", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.TwoMirrorHexGlass()),
                new SceneDefinition("procedural", "3 程序万花筒", SceneKind.Dome, go => go.AddComponent<ProceduralKaleidoscopeScene>()),
                new SceneDefinition("mandala", "4 曼陀罗", SceneKind.Dome, go => go.AddComponent<MandalaScene>()),
                new SceneDefinition("julia", "5 Julia 集", SceneKind.Dome, go => go.AddComponent<JuliaScene>()),
                new SceneDefinition("mandelbulb", "6 Mandelbulb", SceneKind.Object, go => go.AddComponent<MandelbulbScene>()),
                new SceneDefinition("tree", "7 分形树", SceneKind.Object, go => go.AddComponent<FractalTreeScene>()),
                new SceneDefinition("lorenz", "8 Lorenz 吸引子", SceneKind.Object, go => go.AddComponent<LorenzScene>()),
                new SceneDefinition("phyllotaxis", "9 叶序螺旋", SceneKind.Dome, go => go.AddComponent<PhyllotaxisScene>()),
                new SceneDefinition("mandelbrot", "10 Mandelbrot 深潜", SceneKind.Dome, go => go.AddComponent<MandelbrotZoomScene>()),
            };
        }
    }
}
