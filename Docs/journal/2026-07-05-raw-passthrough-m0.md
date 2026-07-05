# 2026-07-05 · raw 复刻 seethrough — 设计 + M0 几何地基

## 做了什么
用户目标澄清：**不调用系统 seethrough，仅用现有 raw 双目鱼眼 + 标定 + 位姿，自建与 seethrough 视觉等效的透视画面**。据此：

1. **设计文档** `Docs/designs/2026-07-05-raw-passthrough-reprojection.md`：把 seethrough 分解为「去畸变 + 逐像素深度 + 视点重投影 + 出光时刻 timewarp」四要素，盘点现有条件（内参✅、外参⚠️拿得到没用、深度❌需自建、合成层❌需接），定 M0-M3 里程碑与验收。

2. **M0 几何地基**（编辑器可验证部分全部完成）：
   - `Assets/Main/Core/Rendering/EyeReprojection.cs`（新）：视点重投影核心 `CameraRayForEyeRay`（眼视线+深度+眼→相机平移 → 相机采样方向）。纯 C#，是 M1/M2 视差的心脏。
   - `EyeReprojectionTests.cs`（新，5 测）：零平移退化、深度→∞ 视差消失、有限深度视差偏移、与 `FisheyeProjection` 组合、near-axis 无 NaN。
   - `FisheyeCalibration`：+`extrinsicTranslation` 字段 + `SetFromSdkExtrinsics(cameraToHead, eyePosInHead)`。
   - `FisheyeDomeRenderer.PushParameters`：推 `_LeftCamOffset/_RightCamOffset/_Radius`。
   - `FisheyeDome.shader`：新增 `CameraRay()`（逐行照抄 `EyeReprojection`），frag 先重投影再采样。默认 camOffset=0 + radius=20 → 行为与改前完全一致（安全）。
   - `VstCameraDomeFeeder` / `FisheyeDomeXRRig`：**WorldLocked → HeadLocked**（穹顶跟头位置+朝向，实时自视角必需，消转头漂移）；feeder 拿到 `GetCameraExtrinsicsfor4U` 后一次性喂入真实外参替换 identity 并重推参数。

## 为何这么改
- 之前 XRLive 画面与真实世界错位的三根因：①外参写死 identity（相机安装倾角被忽略→整体倾斜）②WorldLocked 用在实时头显（转头漂移）③固定 20m 半径无深度（近景视差错）。
- M0 修 ①②，并把 ③ 的架构入口（每像素深度）铺好：shader 的 `_Radius` 常量深度就是 M1 起要替换成逐像素深度的位置。「固定穹顶 = 深度∞ 退化」这点由 `EyeReprojection` 单测钉住。

## 测试结果
- 编译：Success, No errors（compile-unity-flow，15.5s）。
- EditMode：**67 passed / 0 failed**（含新增 5），写入 `.gates/tests-green`。
- PlayMode：**5 passed / 2 skipped**。skip 的 2 个是 AsyncGPUReadback 像素回读（当前管线不支持，`Assert.Ignore`）——即 shader 的**像素级视觉校验，属设备门**，编辑器环境本就跳过。

## 遗留（诚实边界）
- **M0 真机视觉未验**：远景是否重合、倾斜是否消失、转头是否稳定，须 PICO 4U（Enterprise 激活）上机看。journal 历史标注设备 pending。
- **外参手性未核**：`SetFromSdkExtrinsics` 假设 SDK 4×4 已在 Unity 左手系、且为 camera→head；若错则平移/旋转符号翻转，真机一看便知，改一行。
- **M1-M3 未动**：深度面（spatial mesh / 立体匹配）、合成层 timewarp 均为设备门 + 大工程，按宪法「两级测试不全绿不请求验收」应在设备到位后逐里程碑做、逐里程碑真机验。M2 实时鱼眼立体匹配是重活（GPU 预算 + 研究性）。
- **承诺线**：远中景≈seethrough、近景静态对齐、近景动态接近；**不承诺像素级完全一致**（PICO 不开放稠密深度的硬边界）。

## 下一步
设备到位后：先真机验 M0（远景+转头），再进 M1（spatial mesh 打底）。未验证的里程碑不入「完成」。
