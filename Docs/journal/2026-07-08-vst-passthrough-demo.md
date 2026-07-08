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

---

## 追加（同日）：Builder 重构为场景注册表（每场景独立打包）

需求：参考 YC-Ego `BuildScript.cs`，让现有几个场景都能分别打包。

- **`Builder.SceneRegistry`**（公开注册表，key → 场景路径 → APK 名）驱动一切：统一菜单 `PicoTest/Build APK/*` 五项——`xrlive`(VstLive) / `vstpassthrough` / `webrtc` / `trackerimu`（保留 ENABLE_BODY_TRACKING 命名分流）/ `xrdemo`(静帧立体，**新增入口**)。`FisheyeDomeDemo` 不注册：纯 PC 扫视 demo 且依赖 gitignore 的人物帧，出 APK 无意义（代码注释记录）。
- **通用 batchmode 入口** `Builder.BuildSceneApk -scene <key> [-outputPath ...]`（退出码 0/1/2），照抄 YC-Ego 的 `ClearScriptAssembliesAndRefresh`（batchmode 不刷新 AssetDatabase 且缓存陈旧程序集 → 改的代码可能不进 APK，YC-Ego troubleshooting §9.6.1）；`BuildVstLiveTest`（dev 构建）/`BuildPico`（整包）保留兼容并同样补了防陈旧处理。
- **菜单构建成功后 `RevealInFinder` 定位产物**（YC-Ego 交互习惯）；`xrdemo` 构建时缺 `sbs_frame.png` 提前警告（构建会成功但真机无帧）。
- **旧菜单迁移**：`Build VST Live (in-editor)`/`Build WebRTC Dome (in-editor)`/`Tracker IMU/Build APK (in-editor)`/`VST Passthrough/Build APK` 全部并入 `Build APK/*`；同步更新 Exp-TrackerIMU、Exp-VstPassthrough 的 README 与场景生成器日志文案（journal/plans 历史文档不动）。VstPassthrough 实验内的重复构建菜单移除，归口中央注册表。
- **守护测试** `BuildSceneRegistryTests`（EditMode +3）：注册场景必须存在于磁盘（场景改名先红测试，而非点菜单才炸）、key 唯一、APK 名唯一且 .apk 结尾。`Tests.EditMode` asmdef 增引 `PicoTest.Editor`。
- 测试：**EditMode 98 / PlayMode 8 全过**（1 skip 为既有 HEVC 探针），tests-green 已写。
- 未采纳 YC-Ego 的：时间戳 APK 名（本项目约定固定名 + install-latest-apk.ps1 按修改时间挑最新，改名会破坏文档/脚本约定）、keystore 锁定与 OTA 产物（本项目无 OTA 需求）。

## 追加2（同日）：Install APK 子菜单（按场景装机）

- 与 Build APK/* 对称的 `PicoTest/Install APK/*`（YC-Ego 按变体分开装机模式）：每场景一项按注册表 APK 名**精确安装**（避免"全局最新"把别的场景包装上设备）+ `Latest - 最新构建（不限场景）`（原 `Install Latest APK + Launch` 迁入）。缺包弹窗提示先构建；TrackerImu 按当前体追 define 装对应变体。全部复用 `Tools/install-latest-apk.ps1 -Path ... -Launch`（本就支持 -Path，首次实际启用）。
- 实测：用户经新菜单构建 vstpassthrough 成功（Succeeded, 67.7MB）；装机路径端到端工作，失败仅因 `no adb device connected/authorized`（设备未连，环境问题非代码）。
- 测试：EditMode 98 / PlayMode 8 全绿。

## 追加3（同日）：真机部署成功 + ImuCamRig 真机坐实

- **装机踩坑**：设备（PA9410MGL**5121119G**，非之前的 …349G）上有签名不一致的同包名旧包 → `INSTALL_FAILED_UPDATE_INCOMPATIBLE`。`adb uninstall com.wuyazishidi.picotest` 后重装成功。多台构建机/多台设备混用时会反复遇到，记住先卸后装。
- **VstPassthroughDemo 真机启动全绿**（logcat）：透视 via PXR_Manager 开启；标定就绪 **T 判读=cam_to_imu 共线度=0.9999 基线=64.1mm 左相机头系位置 x=−0.032**（ImuCamRig 的 PC 测试结论在真机数据上坐实）；VST 相机 RAW 流 2560×960@30 首帧即到；mode=capture radius=1.5 latency=80ms ext=calib。
- SDK 运行时报的相机外参平移与烤入的 cam_calib.json 一致 → 标定文件与本台设备匹配（换机顾虑排除）。
- 待人工验收（设计 §验收标准三组对比）：A 键 vs 原生透视对齐、mode capture vs head 稳定性、ext calib vs id 改善。

