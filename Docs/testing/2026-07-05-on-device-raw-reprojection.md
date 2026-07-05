# 真机验证清单 · 纯 raw 视点重投影 Demo

分支 `raw-reprojection-demo`。前置：PICO 4 Ultra + Enterprise 相机激活；构建 APK 部署（编辑器无相机=黑屏属预期）。
场景 `Assets/Main/Scenes/RawReprojectionDemo.unity`（含 `RawReprojectionFeeder` + `RawReprojectionDiagnostics`）。

## 手柄实时调参（免重打包，`RawReprojectionDiagnostics`）
| 键 | 作用 | 什么时候按 |
|---|---|---|
| 左 X | 切 flipV | 画面上下颠倒 |
| 左 Y | 切 mirror | 左右镜像 / 文字反了 |
| 右 A | 翻转左右外参平移符号 | 视差方向反了（近物体错位方向不对）→ 定手性 |
| 右 B | 常量深度 1.5/5/20m 循环 | 看视差量级是否合理 |

日志（logcat / `adb logcat -s Unity`）每 2s 打印：`[Diag] frames=… bpp=… extrinValid=…` 与外参平移值。

## M0 验证（常量深度，地基）— 必须先全过
拉 APK：`Tools/install-latest-apk.ps1 -Launch`。看 logcat：

- [ ] **相机出帧**：`[VST] FIRST FRAME w=… h=… bpp=4`；`[Diag] frames` 持续增长。bpp≠4 → RAW 格式不对。
- [ ] **外参已应用**：`[RawReproj] 外参已应用 L.t=… R.t=…`（非零）。若一直不出 → `GetCameraExtrinsicsfor4U` 未成功，视差不可用。
- [ ] **不黑屏 / 不全花**：穹顶中心有真实画面（纯 raw：边缘超 FOV 为黑，正常）。
- [ ] **朝向正确**（用左X/左Y定）：
  - 水平边（桌沿/门楣）保持水平；垂直边保持垂直。
  - 文字不镜像（镜像→按左Y）。
  - 画面不上下颠倒（颠倒→按左X）。预期 flipV 初值=1（相机 top-down）。
- [ ] **不倾斜**：正视前方，地平线不歪。歪 = 外参旋转手性问题，记录待改 `SetFromSdkExtrinsics`。
- [ ] **转头稳定**（HeadLocked）：转头时固定的真实物体停在其真实世界方位，不漂移/不甩动/不反向。
- [ ] **远景角度合理**：~5m 物体的角直径/方位与肉眼预期一致（可短暂掀起头显目测对比）。
- [ ] **手性/视差方向**（右A + 右B）：右B 切到 1.5m，看近处物体是否往正确方向偏移；偏反 → 右A 翻符号。记录最终符号，回填代码。

> M0 全过 = raw→显示的角度管线正确。**未过不进 M1**（宪法：两级测试不全绿不请求验收）。

## M1 验证（spatial mesh 深度面，近景静态对齐）
前置：场景加 PICO `PXR_SpatialMeshManager`（meshPrefab 带 MeshCollider，置于某层，如 "SpatialMesh"）；`RawReprojectionFeeder.depthMode=SpatialMesh`、`spatialMeshLayers` 设该层。

- [ ] 空间网格生成：房间扫描后 mesh GO 出现（带 MeshCollider）。
- [ ] **近景静态对齐**：桌面/墙（0.5–1.5m）比 M0 常量深度明显更贴合真实位置（视差被校正）。
- [ ] 望向空处（无网格）→ 回退远景深度，不炸、不闪。

## M2 验证（GPU 立体匹配，近景动态/手）— 需先实现 GPU 匹配
`StereoDepth`（`z=fx·B/d`）已就绪并测过；缺的是产视差的 GPU 管线（鱼眼→校正 remap → 立体匹配）。

- [ ] 视差图非空、时域稳定。
- [ ] **手（0.3m）对齐**：伸手，手在头显里与真实手位置重合（M0/M1 都做不到）。
- [ ] 60fps 预算内（掉帧→降分辨率/时域滤波）。

## 已知待定（真机定论后回填代码）
- `SetFromSdkExtrinsics`：SDK 4×4 手性/基准（camera→head？Unity 左手系？）——右A 试出符号后固化，去掉运行时翻转。
- `flipV`/`mirror` 初值：左X/左Y 试出后写死场景/feeder 默认。
- `coverageDeg`：默认 146°（匹配相机水平 FOV）；若黑边过多/内容被裁再调。
