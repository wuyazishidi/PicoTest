# 2026-07-16 Robot Stream Left/Stereo Preview 换真实 DS 标定

分支：`fisheye-stereo-dome`。承接同一天的 `Exp-RobotStreamLeftPreview`（左目）/
`Exp-RobotStreamStereoPreview`（双目）——建完之后用户问"目前标定用的是哪个"，答案是两个 demo
当时都还在用 Pico 头显自己的 `cam_calib.json`（经 `RobotCalib`）当机器人相机的占位，等距鱼眼模型。
但 `Exp-RobotDsDome`（同一天更早完成）已经查清机器人真实相机是 **Double Sphere 模型**，用等距鱼眼
在光学上是错的。用户要求换成真实标定。

## 做了什么

两个 demo 的标定+渲染管线整体替换：

| 换之前 | 换之后 |
|---|---|
| `FisheyeCalibration`（等距鱼眼，fx/fy/cx/cy + k1~k6） | `DsEyeCalibration`（Double Sphere，fx/fy/cx/cy + xi/alpha），来自 `Exp-RobotDsDome/Calibration/RobotDsLeft(Right).asset`（真实 `3-camchain.yaml`） |
| `FisheyeDomeRenderer` + `FisheyeDome.shader`（Main） | `DsDomeRenderer` + `RobotDsDome.shader`（`Exp-RobotDsDome`，DS 前向投影） |
| 运行时读 `StreamingAssets/cam_calib.json` → `RobotCalib.BuildEyeCalibrations`（Pico 参数当占位，外参经 `ImuCamRig` 换算，`useCalibExtrinsics` 开关对照） | 直接引用编辑器里指定的 `DsEyeCalibration` 资产（同 `RobotDsDomeFeeder` 的既有模式，标定是静态导入的，不是运行时解析） |
| `coverageDeg` 默认 146°（等距鱼眼镜头量级） | `coverageDeg` 默认 190°（DS 是宽鱼眼，同 `RobotDsDome` 的起点值） |

传输层（`HttpOfferVideoSource`）、颜色修正（`SwapRB.shader`）、cmd 调参、capture 位姿补偿、A/B 对比、
HUD、安全退出——这些跟标定模型无关的部分**全部不动**。两个 feeder 各删掉一个字段/方法：
`useCalibExtrinsics` 开关和 `LoadCalibration()`/`ApplyExtrinsics()` 整段——DS 标定是外部静态导入的
资产，没有运行时 JSON 解析和"真实/单位阵外参"对照这回事了（跟 `RobotDsDomeFeeder` 保持同款极简）。

asmdef 改动：两个 demo 的主 asmdef + Editor asmdef + PlayMode Tests asmdef，把
`Experiment.RobotStream`（RobotCalib 所在，不再用）换成 `Experiment.RobotDsDome`（DsEyeCalibration/
DsDomeRenderer 所在）。`Experiment.RobotStreamStereoPreview` 对 `Experiment.RobotStreamLeftPreview`
的引用（复用 `HttpOfferVideoSource`）不变。

场景生成器同步改：从 `Assets/Main/Settings/Calibration/RealLeft(Right).asset`（Pico 出厂标定）
改读 `Assets/Experiments/Exp-RobotDsDome/Calibration/RobotDsLeft(Right).asset`（机器人真实标定），
并加了 `EnsureShaderIncluded()`（照抄 `RobotDsDomeSceneBuilder` 的防御——`RobotDsDome.shader` 若不在
Always Included Shaders 里，真机剥离→黑屏；虽然理论上 `Exp-RobotDsDome` 首次搭建时已经注册过，
这里幂等地再确认一次，零成本）。

PlayMode 冒烟测试的 `MakeCalib()` helper 换成 `DsEyeCalibration`（`xi=0, alpha=0.5`，抄
`DsDomeRenderSmokeTests.Cal()` 的既有惯例：前向光线不受 fx/cx 影响，只要让 `DsDomeRenderer` 初始化
不报错）。

## 为何这么做

- **不复制 DS 数学/shader**：`DoubleSphereProjection`/`RobotDsDome.shader`/`DsDomeRenderer`/
  `DsEyeCalibration` 都已经在 `Exp-RobotDsDome` 里写好、单测覆盖过（golden 值对 Python `ds_project`），
  两个新 demo 直接引用该实验的 asmdef，不重新实现一遍——同"另起炉灶但最大化复用"的一贯做法。
- **不做等距鱼眼/DS 双模型开关**：两个 demo 的目的就是接真实机器人画面，机器人相机模型是确定的
  （DS），没有"对照旧模型"的需求，保留 `FisheyeCalibration` 兼容层只会增加不必要的分支。
- **标定资产直接引用而非运行时解析**：`DsEyeCalibration` 是 Editor 导入时一次性从 camchain.yaml
  生成的 ScriptableObject（`DsCamchainImporter`），不像 Pico 的 `cam_calib.json` 那样每台设备不同、
  需要运行时读——所以两个 feeder 的标定加载从"运行时读文件+外参换算"简化成"Inspector 直接指资产"，
  这也是抄 `RobotDsDomeFeeder` 的既有设计，不是新发明。

## 测试结果

编译 Success 0 错误。EditMode 119/119、PlayMode 12（+1 skip）/12 全绿（含两个 demo 各自的假源冒烟，
这次假源冒烟实际走的是 `DsDomeRenderer.Initialize()/PushParameters()`，验证了 DS 标定接线没有在
运行时报错/崩溃）。两个场景用各自的 `Build Demo Scene` 菜单重新生成（旧场景文件引用的是已删除的
`fallbackCalib`/`fallbackLeft`/`fallbackRight` 字段，Unity 会静默丢弃未知字段、标定变 null，
所以必须重新生成而非留着旧场景）。`.gates/tests-green` 已更新。

## 遗留 / 下一步

1. **PC 环回肉眼验证**：DS 去畸变在真实源画面上是否自然（直线变直、边缘不畸变），
   `coverageDeg=190` 起点是否合适——`Exp-RobotDsDome` 自己的 journal 也留了同样的遗留项，
   两个新 demo 继承同一个未验证状态。
2. **真机**：同前序 journal，卡在机器人 IP/连接方式未定。
3. 若后续要把 `HttpOfferVideoSource`/`SwapRB` 这两块进一步下沉成更通用的复用单元（比如新建一个
   `Experiment.RobotStreamTransport` 之类），现在三个 demo（RobotStream/LeftPreview/StereoPreview）
   的标定和传输已经是正交的两条轴，具备抽的条件，但目前跨层引用（LeftPreview→RobotDsDome，
   StereoPreview→RobotDsDome+RobotStreamLeftPreview）还不算多，先不动。
