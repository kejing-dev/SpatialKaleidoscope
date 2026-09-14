# 验证截图（v0.3）

- `editor/`：Unity 编辑器内用与真机相同的运行时代码路径渲染，每个场景 8 个视角（back / down35 / down60 / forward / left60up / right60up / up45 / zenith；3D 物体场景为 above / close / front / leftside / rightside / zenith）。
- `emulator/`：PICO 模拟器（swan_oversea_arm64，PICO OS 6，XR 模式）实机运行 APK，通过调试口旋转 XR 原点得到 7 个头部姿态：forward / up45 / zenith / left / right / down / side；截图中的细线是可见的手柄射线。

| 场景 | 编辑器 | 模拟器 |
| --- | --- | --- |
| 1 两镜暗场发光珠 | ![](editor/scene01.jpg) | ![](emulator/scene01.jpg) |
| 2 两镜六重玻璃（水晶暗场） | ![](editor/scene02.jpg) | ![](emulator/scene02.jpg) |
| 3 程序万花筒 | ![](editor/scene03.jpg) | ![](emulator/scene03.jpg) |
| 4 曼陀罗 | ![](editor/scene04.jpg) | ![](emulator/scene04.jpg) |
| 5 Julia 集 | ![](editor/scene05.jpg) | ![](emulator/scene05.jpg) |
| 6 Mandelbulb | ![](editor/scene06.jpg) | ![](emulator/scene06.jpg) |
| 7 分形树 | ![](editor/scene07.jpg) | ![](emulator/scene07.jpg) |
| 8 Lorenz 吸引子 | ![](editor/scene08.jpg) | ![](emulator/scene08.jpg) |
| 9 叶序螺旋 | ![](editor/scene09.jpg) | ![](emulator/scene09.jpg) |
| 10 Mandelbrot 深潜 | ![](editor/scene10.jpg) | ![](emulator/scene10.jpg) |
