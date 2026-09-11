# 验证截图

- `editor/`：Unity 编辑器内用与真机相同的运行时代码路径渲染（PlaybackController.Step + 场景 Tick），每个场景 6 个视角（穹顶场景：天顶 / 仰视 45° / 平视 / 左右 60° 仰视 / 俯视；3D 物体：正面 / 近距 / 左右侧 / 俯视 / 天顶）。
- `emulator/`：PICO 模拟器（swan_oversea_arm64，PICO OS 6，XR 模式，单眼 1536×1536）实机运行 APK，通过调试口旋转 XR 原点得到 7 个头部姿态：forward / up45 / zenith / left / right / down / side。
- `editor/transitions.jpg`：12 个交叉淡入淡出在 0.3 s（半程）的画面，包括 12→1，证明无黑帧。

| 场景 | 编辑器 | 模拟器 |
| --- | --- | --- |
| 1 三镜彩色玻璃 | ![](editor/scene01.jpg) | ![](emulator/scene01.jpg) |
| 2 两镜暗场发光珠 | ![](editor/scene02.jpg) | ![](emulator/scene02.jpg) |
| 3 三镜花瓣羽毛 | ![](editor/scene03.jpg) | ![](emulator/scene03.jpg) |
| 4 两镜六重玻璃 | ![](editor/scene04.jpg) | ![](emulator/scene04.jpg) |
| 5 程序万花筒 | ![](editor/scene05.jpg) | ![](emulator/scene05.jpg) |
| 6 曼陀罗 | ![](editor/scene06.jpg) | ![](emulator/scene06.jpg) |
| 7 Julia 集 | ![](editor/scene07.jpg) | ![](emulator/scene07.jpg) |
| 8 Mandelbulb | ![](editor/scene08.jpg) | ![](emulator/scene08.jpg) |
| 9 分形树 | ![](editor/scene09.jpg) | ![](emulator/scene09.jpg) |
| 10 Lorenz 吸引子 | ![](editor/scene10.jpg) | ![](emulator/scene10.jpg) |
| 11 叶序螺旋 | ![](editor/scene11.jpg) | ![](emulator/scene11.jpg) |
| 12 Mandelbrot 深潜 | ![](editor/scene12.jpg) | ![](emulator/scene12.jpg) |

## 过渡

![](editor/transitions.jpg)
