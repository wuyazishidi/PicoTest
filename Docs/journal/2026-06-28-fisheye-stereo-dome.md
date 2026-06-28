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
