using UnityEngine;
using Kaleido.Scenes;

namespace Kaleido
{
    /// <summary>The fixed 12-item content registry (PRD section 5). Order is part of the product.</summary>
    public static class KaleidoPlaylist
    {
        public static SceneDefinition[] Create()
        {
            return new[]
            {
                new SceneDefinition("glass3", "1 三镜彩色玻璃", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.ThreeMirrorGlass()),
                new SceneDefinition("beads2", "2 两镜暗场发光珠", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.TwoMirrorDarkBeads()),
                new SceneDefinition("petals3", "3 三镜花瓣羽毛", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.ThreeMirrorPetals()),
                new SceneDefinition("glass2", "4 两镜六重玻璃", SceneKind.Dome,
                    go => go.AddComponent<PhysicalKaleidoscopeScene>(), PhysicalKaleidoscopeScene.Presets.TwoMirrorHexGlass()),
                new SceneDefinition("procedural", "5 程序万花筒", SceneKind.Dome, go => go.AddComponent<ProceduralKaleidoscopeScene>()),
                new SceneDefinition("mandala", "6 曼陀罗", SceneKind.Dome, go => go.AddComponent<MandalaScene>()),
                new SceneDefinition("julia", "7 Julia 集", SceneKind.Dome, go => go.AddComponent<JuliaScene>()),
                new SceneDefinition("mandelbulb", "8 Mandelbulb", SceneKind.Object, go => go.AddComponent<MandelbulbScene>()),
                new SceneDefinition("tree", "9 分形树", SceneKind.Object, go => go.AddComponent<FractalTreeScene>()),
                new SceneDefinition("lorenz", "10 Lorenz 吸引子", SceneKind.Object, go => go.AddComponent<LorenzScene>()),
                new SceneDefinition("phyllotaxis", "11 叶序螺旋", SceneKind.Dome, go => go.AddComponent<PhyllotaxisScene>()),
                new SceneDefinition("mandelbrot", "12 Mandelbrot 深潜", SceneKind.Dome, go => go.AddComponent<MandelbrotZoomScene>()),
            };
        }
    }
}
