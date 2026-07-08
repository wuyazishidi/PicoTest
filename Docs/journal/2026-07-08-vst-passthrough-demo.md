# 2026-07-08 VST Passthrough Demo（Exp-VstPassthrough）

分支：`fisheye-stereo-dome`。设计 `Docs/designs/2026-07-08-vst-passthrough-demo.md`。
需求：新建 Demo 优化 XRLiveDemo（=`FisheyeDomeXRLive`），目标 = 用自有 VST 鱼眼→穹顶管线复现 PICO 原生透视效果。**用户明确：另起炉灶，不影响已有 demo** —— 全部代码在 `Assets/Experiments/Exp-VstPassthrough/`，Main 与 XRLiveDemo 零改动。

## 做了什么

| 产出 | 说明 |
|---|---|
| `ImuCamRig`（纯 C#，实验内） | `T_imu_to_cam` → 头系→相机系外参自标定换算；解决 2026-06-30 journal 遗留的"外参坐标系换算未做" |
| `VstPassthroughFeeder` | 头锁定/捕获位姿两模式 + 外参进 shader + adb cmd.txt 调参 + A 键对比原生透视 + B 键安全退出 |
| `VstPassthroughSceneBuilder` | 菜单生成 `VstPassthroughDemo.unity`（已生成入库）+ in-editor Release APK 构建（独立菜单，不动 Builder.cs） |
| `ImuCamRigTests`（EditMode +6） | 真实 cam_calib.json 数据断言 + 合成 imu→cam 用例覆盖另一判读分支 |

## 关键发现（重要，已用测试锁定）

1. **`T_imu_to_cam` 字段名与数据语义相反**：真机 A9410 数据按字段名（imu→cam）解读时，基线方向(x̂_imu)与图像横轴(−ŷ_imu)垂直——双目相机物理上不可能；按 **cam→imu（相机在 IMU 系的位姿）** 解读则基线∥图像横轴（−ŷ）、光轴朝前（−ẑ）、左相机在用户左侧，完全自洽。`ImuCamRig` 对两种解读做基线/图像横轴共线性打分（实测 ≈1.0 vs ≈0）自动选择。
2. **IMU 系轴向**（cam→imu 判读下）：x̂=上、ŷ=用户左、ẑ=后（右手系）。头系基不臆测，直接从标定构造：X=左→右相机（用户右）、Z=光轴均值（前）、Y=X×Z（上）。
3. **有效相机系是 y-up**：现管线（identity 外参 + flipV=1）真机画面正立 ⇒ shader 采样链的有效相机系 y 朝上（OpenCV y-down 被 top-down 缓冲 + flipV 双翻转抵消）。故 R_eye = diag(1,−1,1)·R(imu→cam)·M；两个 det=−1 因子相抵，R_eye 为真旋转（det=+1，可安全转四元数进 `FisheyeCalibration.extrinsicRotation`）。换算后 R_eye 近单位阵（残留几度安装角）——与"identity 大致能看"的既有事实吻合，外参补的正是那几度的逐眼修正。

## 为何这么做

- **capture 模式**（默认）：帧到达时把穹顶锚到 `now−latencyMs` 时刻头位姿（环形缓冲回溯），捕获后的转头被世界锁穹顶自动补偿 ≈ 原生透视 late-stage reprojection 的稳定感来源；`mode head` 保留朴素头锁定作对照。
- **radius 1.5m**（vs XRLive 20m）：旋转-only 近似下 radius ≈ 配准距离，原生透视重投影面即 ~1m 量级；真机 cmd 调。
- **adb cmd.txt 通道**（照抄 Exp-TrackerIMU 模式）：radius/latency/mode/ext/dome/cover/feather/hud/dump 全部免重打包调参——优化循环的核心工具。

## 测试结果

- 编译 Success 0 错误；**EditMode 95（+6）/ PlayMode 8 全过**，1 skip 为既有 HEVC 视频探针（记录在案）。`.gates/tests-green` 已写。
- 真实数据断言全过：cam→imu 判读、基线 0.064m 复原、R_eye 正交 det=+1、近单位阵、相机头系位置 x≈∓0.032m。

## 遗留 / 下一步（真机）

- 构建 APK（菜单 `PicoTest/VST Passthrough/Build APK (in-editor)`）→ 装机 → 按设计 §验收标准做三组对比：capture vs head 稳定性、A 键 vs 原生透视对齐、ext calib vs id 改善。
- 平移补偿未做（v1 旋转-only）：相机-眼位置差数 cm，近距离有残留视差误差；`ImuCamRig` 已输出相机头系位置备用。
- frame.timestamp（SDK ns）与 Unity 时钟换算未接：v1 用固定 latencyMs 近似，真机调出最稳值后可再接真时间戳。
- `cover` 命令只更新 thetaMax（裁剪），穹顶网格弧度改 Inspector 后重启生效。
