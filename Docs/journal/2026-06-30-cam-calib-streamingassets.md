# 2026-06-30 设备相机标定接入 + adb 安装工具 + 控制台报错修复

## 做了什么
1. **控制台 NRE 修复**：URP/ShaderGraph `MaterialPostprocessor` 的两处 `NullReferenceException` 根因是 PICO SDK 样例 `SpatialAudio/Samples/material/unity_logo.mat` 实为未解析的 Git LFS 指针（129B），Unity 读到指针文本→加载失败→后处理炸。网络拉不到 LFS 对象，故按同目录 `pico_logo.mat` 重建为有效标准材质（引用 unity_logo.jpg，.meta GUID 不变）。
2. **`Tools/install-latest-apk.ps1`**：用 adb 把最近构建的 APK（搜 `Builds\`/`Build\`/项目根，取最新）装到设备，多设备 `-Serial`、可选 `-Launch`。实测在 PA9410MGL5140349G 安装成功。UTF-8 BOM。
3. **设备真实标定接入（烤成 StreamingAssets）**：
   - 设备相机参数格式从旧 `metadata.json`（嵌套 `streams.camera.factory_calibration`）改为新 `cam_calib.json`（扁平根级），放 `Assets/StreamingAssets/cam_calib.json`（adb 从 `/sdcard/PicoCalib/` 拉取）。
   - `FactoryCalibrationImporter` 改读 `cam_calib.json`（兼容旧 metadata.json 回退），菜单更名 `(from cam_calib.json)`；实跑导入成功：`wrote RealLeft/RealRight (1280x960), model=equiDis62, baseline=0.064m`。
   - 新增 Core 解析器 `PicoTest.Core.Rendering.CamCalib`（纯 C#/Newtonsoft）→ `FisheyeProjection`（取 D 前 6 项 k1..k6，丢切向 p1/p2）。
   - 设计 note：`Docs/designs/baked-cam-calib.md`。

## 为何
- VST 实时鱼眼穹顶需用本机真实内参/畸变/基线（equiDis62, 1280×960, baseline 0.064m），替代占位参数。

## 测试结果
- EditMode：**68 passed / 0 failed**（含新增 6 个 `CamCalibTests`），`.gates/tests-green` 已写。
- 导入器菜单实跑成功；控制台原 "metadata not found" 报错不再复现（残留为历史日志）。
- PlayMode 尚未跑（提交前需补 `/run-tests` 全绿）。

## 真机首测 → 全黑根因 + 修复
- **现象**：打包安装后头显内全黑。
- **根因**（logcat `Unity:V`）：`ArgumentNullException: shader` @ `FisheyeDomeRenderer.Initialize` ← `VstCameraDomeFeeder.Start`。穹顶材质运行时经 `Shader.Find("PicoTest/FisheyeDome")` 创建，无打包资源引用 → IL2CPP 剥离该 shader → Find 返回 null → Start() 在 `VstCamera.Initialize()` **之前**中断（故 logcat 无任何 `[VST]`，相机根本没启动）。
- **修复**：`PicoTest/FisheyeDome` 加入 `GraphicsSettings.m_AlwaysIncludedShaders`（编辑器助手菜单 `PicoTest/Fix/Ensure FisheyeDome Shader Included`，`Assets/Editor/Rendering/DomeShaderInclude.cs`）。**需重新打包生效**；重打后看 `[VST]` 日志判断相机是否真出帧（第二层未知）。

## 混合转向 Task 7 接线（低速云台伺服）
- 需求：转头超过一定范围才把穹顶低速插值跟到头朝向（保居中、可看 FOV 外）；死区内自由环顾。
- `GazeServo`（带死区+限速+最短路，已测）+ `RobotHeadPoseDriver`（伺服锚点偏航）此前已有但**未接线**。
- 改动：`RobotHeadPoseDriver` 增 `followLocalHead` 模式（首版用 Camera.main 偏航投到锚点父空间求 target yaw，符合设计注解）；`VstCameraDomeFeeder` 经 `AddComponent` 挂载并暴露 `enableGazeServo/servoRateDegPerSec(20)/servoDeadzoneDeg(8)`（纯加法，无场景文件改动）。
- 编译 Success 0 错误；EditMode 68 passed。手感(死区/速率/是否加 pitch)留真机调。

## 遗留
- **外参 R(eye→robotHead) 坐标系换算**（`T_imu_to_cam` OpenCV→Unity 反演）未做，`ToProjection`/导入器外参暂用单位阵，需设备验证。
- 把基线/外参接入双目穹顶（左右眼偏移、IMU↔头节点对齐）。
- Android 运行时从 StreamingAssets 读 `cam_calib.json` 需 `UnityWebRequest`（接入穹顶时实现）。
- 环境注意：开了两个 Unity 编辑器实例（本项目 3212 / YC-Ego 3232）；YIUIMCP 端口随实例变化，脚本须确认端口。
