# 2026-06-28 双目鱼眼球面投影（具身遥操作视角）

分支：`fisheye-stereo-dome`（从 main 切出）。设计 `Docs/designs/2026-06-28-fisheye-stereo-dome.md`，计划 `Docs/plans/2026-06-28-fisheye-stereo-dome.md`。

## 做了什么

把机器人双目鱼眼画面以**角度 1:1 反投影**投到左右眼各自的 220° 反法线穹顶，双目视差给深度。按 TDD 计划完成全部 7 个任务：

| Task | 产出 | 测试 |
|---|---|---|
| 1 | `Main.Core/Rendering/FisheyeProjection`（鱼眼正投影纯数学，shader 镜像） | EditMode +6 |
| 2 | `FisheyeCalibration`(SO) + 左右 220° 占位标定资产 + Editor 生成菜单 | EditMode +2 |
| 3 | `InvertedSphereMesh`（220° 反法线穹顶生成） | EditMode +3 |
| 4 | `FisheyeDome.shader`（URP unlit 立体实例化，照抄 §3 数学） | 导入零报错 |
| 5 | `FisheyeDomeRenderer`（参数推送 + RenderFrame，默认 WorldLocked） | PlayMode +2 |
| 6 | 整链渲染冒烟（URP StandardRequest 回读像素） | PlayMode +1 |
| 7 | `GazeServo`(纯数学) + `RobotHeadPoseDriver`（低速云台伺服，可选） | EditMode +4 |

## 为何这么做（关键决策）

- **角度 1:1 做成可回归单测**：鱼眼正投影数学放 `Main.Core`（零 UnityEngine，秒测），对解析/前向模型 golden 断言 <1px；shader HLSL 逐行照抄同一公式。把"角度对不对"从肉眼玄学降为断言。
- **混合转向**（决策，2026-06-28）：`RenderFrame` 默认 **WorldLocked**（转头在已收 220° 画面内本地瞬时环顾，零新增延迟）+ 低速率云台伺服（`GazeServo`/`RobotHeadPoseDriver` 跟到平均注视方向，保居中、可看背后）。比纯 HeadLocked 更契合"沉浸+同步"——后者每次转头走全回传链路，最易晕。
- **四项设计决策**：混合转向 / 220° 穹顶 / sRGB 不翻转(`_FlipV=0 _Mirror=0`) / 外参相对机器人头(`R_eye=R(eye→robotHead)`)。
- Task 6 用 `RenderPipeline.StandardRequest`+`Camera.SubmitRenderRequest`，**不用 `Camera.Render()`**（SRP 下不支持）。

## 测试结果

完整套件全绿：**EditMode 61 + PlayMode 4**（本特性新增 18 测试，基线 47→65）。
渲染冒烟硬证：喂左红/右绿中心样图，正前方中心像素非黑且红>绿（skipped=0，未被 Ignore）——shader 采样+FOV+几何全链路实证工作。

## 遗留 / 阻塞验收（非阻塞编码）

- **真 1:1 / 真 3D 验收**需两个硬参数（用户提供）：①机器人双相机**基线 mm**（→深度是否 1:1，基线≫IPD 会"微缩世界"）；②**鱼眼标定来源**（OpenCV `cv::fisheye::calibrate` / 厂商）覆盖占位资产。
- **传输层另案**：`_LeftTex/_RightTex` 上游解码（RTSP/WebRTC/UVC）不在本设计范围。
- **真机级**：立体左右分眼、单通道实例化在 PICO 上的实际表现，需 APK 部署后验（当前仅 PC 级单眼路径验证）。
- **未做（YAGNI）**：预测性 reprojection、6DoF 平移视差/体积重建、demo 场景装配。
- 设计/计划仍为分支状态，未合 main；未建 demo 场景（用户要"看效果"时再装配真实样图 + 截图）。

## 下一步候选

- 装一个 demo 场景（Bootstrap 装配 FisheyeDomeRenderer + 样例标定 + 一张真实鱼眼样图）→ 截图给人审。
- 用户给基线/标定 → 填占位资产 → 跑离线 OpenCV 对拍坐实 <1px。
- 合并分支（走 finishing-a-development-branch）。

---

## 追加（同日）：接入真实 PICO 采集数据

用户提供 `Assets/StreamingAssets/camera.mp4`（PICO 4U 实采）+ `metadata.json`。关键发现与处理：

- **畸变模型不是 k1-k4**：`factory_calibration` model=`equiDis62` = 6 径向(k1-k6)+2 切向(p1,p2)。**实测 k1-k4 截断灾难性发散**（θ=1.37 时高阶项互相抵消，截断得 θ_d≈15 vs 真值≈1.22）。故把 `FisheyeProjection`/shader/标定全扩到 **k1-k6（Horner）**，切向 p1/p2 按设计简化丢弃（边缘残差~1-2px）。
- **基线=0.064m≈IPD**：之前悬而未决的硬件问题被数据回答——**3D 深度能真 1:1**。
- **导入器** `FactoryCalibrationImporter`：从 metadata.json 落 `RealLeft/RealRight.asset`（fx582.9, 1280×960, raw 鱼眼 K+D）。
- **SBS 分半**：视频是 stereo side-by-side 单流，shader/renderer 加 `_LeftUVRect/_RightUVRect`，单张 SBS 纹理左半→左眼、右半→右眼（默认全帧向后兼容）。
- **❌ HEVC 编辑器解码受阻（实证）**：Windows 编辑器 VideoPlayer 无法解码 h265 → `"Cannot read file", width=0`。这是系统缺 HEVC Video Extensions，**非代码问题**；PICO 真机 h265 原生可解。探针 `StereoVideoDecodeProbeTests` 写成「能解码→渲染+存 `Artifacts/dome_real.png`，不能→优雅 Ignore」，**装上 HEVC 扩展后自动转为 PNG 输出，无需改码**。
- 测试：EditMode 62 / PlayMode 5（+1 SBS 分半断言）+1 skip（视频探针）。

**决策（用户）**：装 HEVC Video Extensions 后重跑（而非转码/上真机/合成图）。待装好后重跑 `run-tests -Mode PlayMode` → 取 `Artifacts/dome_real.png` 给人审。

**遗留**：HEVC 扩展装好前看不到真实视频上穹顶；真机立体/沉浸仍需 APK 部署验。

---

## 追加2（同日）：ffmpeg 抽帧 → 真实帧去畸变成功 ✅

- 用户授权装 ffmpeg：`winget install -e --id Gyan.FFmpeg`（8.1.2）。装到 `%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe`（PATH 需重启 shell；脚本里直接走 Links 路径）。**ffmpeg 自带 HEVC 解码器，绕过 Unity 编辑器无法解 h265 的限制**。装 HEVC Video Extensions 重启后 Unity VideoPlayer 仍 "Cannot read file"（确认是 Unity 编辑器顽固限制，非系统编解码器）。
- `Tools/extract-fisheye-frame.ps1` 从 camera.mp4 抽一帧 `sbs_frame.png`（2560×960）。
- `RealFisheyeFrameOnDomeTests`：真实帧 + RealLeft/Right(k1-k6) 渲染穹顶 → `Artifacts/dome_real.png`。
- **肉眼审通过**：鱼眼桶形畸变被正确去除为自然透视——双手在两侧、脚在中央、垃圾桶边缘是直的、地面平整、手不变形。`flipV=0` 朝向自然（flipV=1 会上下颠倒）。SBS 左眼取左半正确。**"角度 1:1 还原"端到端坐实**。
- **宪法 #12**：camera.mp4 / metadata.json / sbs_frame.png（含可识别人物）全部 gitignore 不入库；依赖它们的测试缺数据即 Ignore。标定数值已抽进 RealLeft/Right.asset（纯数字，已入库）。
- 测试：EditMode 62 / PlayMode 6（+1 真实帧渲染）+1 skip（视频探针，HEVC 运行时仍不可解）。

**结论**：核心目标"鱼眼→穹顶 1:1 还原"用真实数据验证成功（PC 端单眼）。真 3D 立体/沉浸仍待 APK 上 PICO（基线 0.064m≈IPD 已确认能支持）。

---

## 追加3（同日）：移除 CurvedUI + 搭 XR 版 demo

- **移除 CurvedUI**：全项目零引用（代码/场景/预制体均无，asmdef 隔离使 Main 不可能引用），曲面早由自写 `InvertedSphereMesh` 提供。直接删 `Assets/Plugins/CurvedUI`（本就未入 git）。编译+测试仍绿，无任何破坏。
- **XR 版 demo**：`FisheyeDomeXRRig` + `FisheyeXRDemoSceneBuilder`（菜单 PicoTest/Build Fisheye XR Demo Scene）。用 XRI Device-based XR Origin（头显追踪相机），穹顶跟头位置/世界锁朝向。shader 的单通道立体实例化 → 真机左右眼各采 SBS 半幅 = 真立体。帧用 UnityWebRequest 跨平台加载。
  - **踩坑**：穹顶 `ZWrite Off` 是背景，XRI 相机默认 clearFlags=Skybox → 天空盒盖住穹顶。修：运行时把 XR 相机 clearFlags 设 SolidColor 黑（遥操作背景本就该黑）。
  - **踩坑**：EnterPlayMode 后忘记 StopPlayMode → 编辑器卡 Compiling（PlayMode 阻塞域重载）。教训：自动化进 PlayMode 后必须确保 StopPlayMode。
  - 编辑器实测单眼预览渲染正确（game view 截图）；真立体只能上真机验。
- **下一步真机**：构建 APK → adb 部署 PICO。注意 YIUIMCP 要编辑器开着、batchmode 构建要编辑器关着（互斥）。真机上把静态帧换成机器人双目流实时纹理。
