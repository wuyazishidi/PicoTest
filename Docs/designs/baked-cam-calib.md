# 设计：设备真实 VST 鱼眼标定烤成内置资源

## 目标
把当前 PICO 4 Ultra Enterprise 的 VST 鱼眼双目标定（`/sdcard/PicoCalib/cam_calib.json`）作为内置资源进入工程，供 fisheye-stereo-dome 渲染使用真实内参/畸变/基线，而非占位参数。

## 方案
- **烤成内置资源**（人审选定的接入方式）：`Assets/StreamingAssets/cam_calib.json`（随包原样文件，便于不重编脚本即可替换；优于 Resources 序列化 blob）。运行时：编辑器/PC 直接读 `Application.streamingAssetsPath`；Android 在 APK jar 内，需用 `UnityWebRequest` 读取（接入穹顶时实现）。
- **Core 解析器** `PicoTest.Core.Rendering.CamCalib`（纯 C#，Newtonsoft，零 UnityEngine）：
  - DTO 对齐 schema：`model / distortion_param_order / resolution_per_eye_wh / stereo_baseline_m / left|right{K,D,T_imu_to_cam,K_rectified}`。
  - `Parse(json)` 带校验（缺 left/right、缺分辨率、D<6 抛 `FormatException`）。
  - `ToProjection(eye, thetaMax, rEyeRowMajor=null)` → 复用既有 `FisheyeProjection`：畸变取 `D[0..5]=k1..k6`，**丢弃切向 p1/p2**（`D[6],D[7]`，量级 ~1e-3），与 `FisheyeProjection` 既定简化一致。

## 标定关键值（本机 PA9410MGL5140349G）
- 模型 `equiDis62`（等距鱼眼），畸变 `k1..k6,p1,p2`；单眼 1280×960；基线 0.064 m。
- 左眼 K：fx≈582.93, fy≈576.11, cx≈635.13, cy≈481.94；右眼略异。
- 每眼另含 `T_imu_to_cam`(4×4 外参) 与 `K_rectified`（去畸变 pinhole，fx=fy≈367, cx=640, cy=480）。

## 验收标准
- EditMode：`CamCalibTests` 全绿（读 `Application.streamingAssetsPath/cam_calib.json`）—— 解析头部字段、左眼内参/畸变（D 长度 8、D[0] 精确）、左右眼区分、光轴→主点、`ToProjection` 用 D[0..5] 且与 p1/p2 无关、坏 JSON 抛异常。
- 编译零错误；`/run-tests -Mode EditMode` 全绿。

## 遗留（需后续设计 + 设备验证，本次不臆测）
- **外参 R(eye→robotHead) 的坐标系换算**：`T_imu_to_cam` 为 OpenCV 约定（Y-down/Z-forward），需反演并转 Unity 左手系；目前 `ToProjection` 的 R 默认单位阵，由调用方显式传入。
- 把基线/外参接入双目穹顶（左右眼 dome 偏移、IMU↔头节点对齐）与 `FisheyeCalibration` ScriptableObject 的从 JSON 自动填充。
- 运行时是否需要设备覆盖（当前为纯烤入；多设备场景另议）。
