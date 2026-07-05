# 2026-07-05 · 纯 raw 视点重投影 Demo（独立分支，另起炉灶）

## 背景
远端 `fisheye-stereo-dome`（领先 30 提交）走了「WorldLocked 云台 + 系统透视透出」的混合路线。用户要另起炉灶做一个**纯 raw 自建**的独立 Demo，不影响远端。分支 `raw-reprojection-demo`，全新文件，不碰远端的 `FisheyeDome.shader`/`FisheyeDomeRenderer`/`VstCameraDomeFeeder`。

## 做了什么（全新文件）
- `Assets/Main/Shaders/ReprojectionDome.shader`：纯 raw 视点重投影穹顶。**深度烤进顶点位置**（顶点 = dirHat×深度，米制）；frag `d = posOS − camOff`（= `EyeReprojection.CameraRayForEyeRay`）→ 鱼眼正投影 → 采样。超 FOV / 出图 → **黑**（不透明，不露系统透视）。立体实例化。
- `IDepthSurface.cs`：深度面接口 + `ConstantDepthSurface`（M0，退化无穷远）。
- `SpatialMeshDepthSurface.cs`：M1 深度面，对 PICO spatial mesh 射线投射取深度（两眼共用世界几何）；无网格/未命中→回退远景。纯 `Physics.Raycast`，无 PICO 程序集编译依赖，编辑器可跑。
- `ReprojectionDomeRenderer.cs`：建单位穹顶 → 按深度面位移顶点 → 推标定/camOffset。含纯函数 `Displace`（可测）。
- `RawReprojectionFeeder.cs`：VST raw 流 → 重投影穹顶；**HeadLocked**（跟头位置+朝向）；运行时喂真实外参；深度模式可切 Constant/SpatialMesh。纯 raw（背景黑，不开系统透视）。
- `RawReprojectionSceneBuilder.cs`：菜单 `PicoTest/Build Raw Reprojection Demo` → 生成 `RawReprojectionDemo.unity`。
- `ReprojectionDomeTests.cs`：深度位移 4 测（常量深度/方向保持/spatial 回退/null 守护）。
- 复用：`EyeReprojection`（M0）、`FisheyeCalibration.extrinsicTranslation`（M0）、`InvertedSphereMesh`、`FisheyeProjection`。
- 取远端最新**重标定** `RealLeft/RealRight.asset`（fx 585.6，替换旧 582.9）。

## 架构要点
「深度烤进顶点位置」让 M0（常量深度）和 M1（spatial mesh 位移）**共用同一套 shader/renderer**：顶点在真实深度 → XR 眼相机渲染出眼视差；`posOS−camOff` 给相机正确采样方向。两个效果正交叠加 = 视点重投影 = seethrough 几何。

## 测试结果
- 编译：Success, No errors。
- EditMode：**71 passed / 0 failed**（+4 `ReprojectionDomeTests`），写 `.gates/tests-green`。
- PlayMode：5 passed / 2 skipped（skip=GPU 回读设备门）。
- **场景 + shader 实编译验证**：跑构建器生成场景，进 PlayMode 让 feeder 初始化——`ReprojectionDomeRenderer.Initialize`（Shader.Find + 建材质 + 顶点位移 + 推参数）**无异常通过**，无 shader 报错（若 shader 缺失/解析错会在建材质抛 ArgumentNull，未发生）。VST 在编辑器 InitEnterpriseService→False（无 token，符合预期）。

## 遗留（诚实边界）
- **真机视觉未验**：远景 1:1 / 转头稳定 / 倾斜消失，须 PICO 4U 上机；编辑器无相机=黑屏属预期。
- **外参手性未核**：`SetFromSdkExtrinsics` 假设 SDK 4×4 为 camera→head 且 Unity 左手系，错则符号翻转，真机一看便知。
- **M1 spatial mesh 未真机验**：射线投射逻辑编辑器可跑（无网格→回退），真机需接 PICO 空间网格并生成 MeshCollider（或 PXR_SpatialMeshManager）。
- **分支基座**：基于 M0（旧 fisheye-stereo-dome + M0），非远端最新整合；仅单独取了重标定资产。与远端云台/透视方案并行，未来若合并需对齐方向。

## 追加：M2 立体深度核心（确定性部分）
- `Assets/Main/Core/Rendering/StereoDepth.cs`：校正空间 视差→深度→3D 点（`z=fx·B/d`、`PointFromDisparity`），纯 Core 可测。用真机值 K_rectified.fx≈371.4 / 基线 0.064m。
- `StereoDepthTests.cs`（6 测）：深度公式、单调性、无效视差守护、主点/偏轴反投影。
- 定位：完整 M2 = raw 鱼眼→去畸变到 pinhole（GPU remap）→极线立体匹配得视差（GPU，重活/设备门）→本类换 3D 点/深度（确定性，已测）→外参转头系→喂深度面。**GPU 匹配与鱼眼校正重采样仍是设备+性能门。**
- M1 现状：`SpatialMeshDepthSurface` 已能消费任意层的 MeshCollider；PICO 有现成 `PXR_SpatialMeshManager`（生成带 MeshCollider 的网格 GO + MeshAdded 事件）→ M1 剩场景配置（加管理器+设层），属设备门，非编辑器可验代码。
- EditMode：77 passed / 0（+6）。

## 下一步
真机验 M0 常量深度（远景+转头）→ 配 PICO spatial mesh 验 M1 近景视差 → M2 接 GPU 立体匹配产视差喂 StereoDepth。
