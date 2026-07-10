# 2026-07-10 Robot DS Dome Demo（Exp-RobotDsDome）

分支：`fisheye-stereo-dome`。设计 `Docs/designs/2026-07-10-robot-ds-dome-demo.md`。
需求：拿到机器人真实双目相机参数（`StreamingAssets/3-camchain.yaml`）+ 实拍视频（`episode_000000.mp4`）+ 工具（`Tools/undistort_ds.py`），参考 VstPassthroughDemo 另起独立 demo 测试，不影响其他 demo。

## 关键发现：相机模型是 Double Sphere，不是等距鱼眼

机器人相机 `camera_model: ds`（Double Sphere，Usenko 2018），内参 `[xi, alpha, fx, fy, cx, cy]`、无单独畸变系数、1920×1080/眼、基线≈55.7mm。**投影数学与 VstPassthrough 的 equiDis62 完全不同**——不能复用 FisheyeDome，必须新写一套 DS shader。这是本 demo 的核心工作。

## 做了什么

全部在 `Assets/Experiments/Exp-RobotDsDome/`，只引用 Main（穹顶网格/退出），不改其他 demo：

| 产出 | 说明 |
|---|---|
| `DoubleSphereProjection`（纯 C#） | 照抄 `undistort_ds.py` 的 `ds_project`（3D 光线→像素+valid）。golden 测试 |
| `RobotDsDome.shader` | DS 前向投影穹顶（逐行照抄 C#）+ 单通道立体实例化 + SBS 分半 + 羽化 + flipV/mirror |
| `DsDomeRenderer` | 结构同 FisheyeDomeRenderer，推 DS uniform（xi/alpha/w2/内参/外参） |
| `DsEyeCalibration`(SO) + `DsCamchainParser`（纯 C#）+ `DsCamchainImporter`（菜单） | yaml → 左右标定资产 |
| `RobotDsDomeFeeder` | VideoPlayer 播 episode mp4 → 穹顶；capture/worldlocked 位姿、cmd 调参、A 键对比原生透视、B 退出、HUD |
| 场景生成器 + Builder 注册 `robotds` | Build/Install APK 菜单自动出现；shader 入 Always-Included |

## 为何这么做（关键决策）

- **DS 投影可回归单测**：用 Python 原版 `ds_project` 按 cam0 真实内参算 golden 值，C# 对之 <0.01px（中心光线→(cx,cy)、背后光线 valid=0），shader 逐行照抄同一函数。把"DS 投影对不对"从肉眼降为断言（同 VstPassthrough 对 FisheyeProjection 的纪律）。
- **不缩放内参**：标定 1920×1080/眼，SBS 视频每眼 960×540（等比 0.5 降采样）。shader 用归一化 UV（u/1920, v/1080），降采样纹理天然对齐，无需缩放（简化且无误差）。
- **方向约定**：DS `ds_project` 用相机系 y-down（OpenCV），穹顶方向 y-up → shader 内 `c.y=-c.y` 转换（投影函数本身保持与 Python 一致，翻转在外层，不污染 golden 断言）；纹理 flipV/mirror 经验校正（默认 flipV=1）。
- **VideoPlayer 播 h264**：视频是 h264（非之前踩坑的 h265），编辑器能解码 → 可肉眼验证去畸变（不必上真机）。
- **外参 v1 单位阵**：camchain 只给 cam 间相对位姿（`T_cn_cnm1`，旋转≈单位、toe<0.3° 可忽略），无 cam→头外参；各眼采各相机，立体深度来自内容。

## 测试结果

- 编译 Success 0 错误；**EditMode 116（+14：DS golden 11 + camchain 解析 3）/ PlayMode 10（+1 DS 渲染冒烟）全过**，1 skip 为既有 HEVC 探针。`.gates/tests-green` 已写。
- **DS 渲染冒烟通过**：RobotDsDome.shader 编译 + 前向中心采样正确（SBS 左眼采左半红）——shader 端到端实证工作。
- `DsCamchainImporter` 实跑成功：RobotDsLeft/Right.asset 生成，基线 55.7mm。

## 遗留 / 下一步

- **编辑器肉眼审**：Play RobotDsDomeDemo → 看 episode 视频上穹顶去畸变是否自然（直线变直、边缘不畸变）；flipV/mirror 若朝向不对经 cmd 调（设计验收 #3）。
- **真机**：`Build APK/Robot DS Dome` → 装机 → 立体分眼、转头稳定、A/B 对比、cover/flip 调。
- **DS FOV vs 穹顶覆盖角**：alpha≈0.57/xi≈0 是宽鱼眼（FOV 可能 >190°），coverageDeg=190 起，真机 cmd 调。
- 与 Exp-RobotStream 关系：那个测 WebRTC 传输、这个测 DS 光学；真机器人 = 两者合流。
- `episode_000000.mp4`（19MB，可能含人）按宪法 #12 gitignore；yaml/工具/baked SO（纯数字）入库。缺视频时 feeder 优雅降级（VideoPlayer 报错日志 + 穹顶空）。
