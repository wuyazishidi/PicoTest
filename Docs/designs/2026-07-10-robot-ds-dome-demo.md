# Robot Double-Sphere Dome Demo（Exp-RobotDsDome）

日期：2026-07-10　分支：`fisheye-stereo-dome`　状态：实验（未晋升）
关联：`Docs/designs/2026-07-08-vst-passthrough-demo.md`（显示方案来源）、`Docs/designs/2026-07-08-robot-stream-passthrough-notes.md`（机器人接入推演）

## 目标

拿到机器人真实双目相机参数（`StreamingAssets/3-camchain.yaml`）+ 实拍视频（`StreamingAssets/episode_000000.mp4`）后，**参考 VstPassthroughDemo 另起一个独立 demo** 把机器人画面投到穹顶做测试。**不影响其他 demo**：全部代码在 `Assets/Experiments/Exp-RobotDsDome/`，只引用 Main（穹顶网格/退出等），不改 Main / 其他实验。

## 与 VstPassthrough 的根本差异：相机模型不同

VstPassthrough/FisheyeDome 用**等距鱼眼 equiDis62**（Kannala-Brandt，θ 的 6 径向多项式）。机器人相机是 **Double Sphere（ds）模型**（Usenko 2018），投影数学**完全不同**，shader 不能复用——这是本 demo 的核心工作。

- `camera_model: ds`，内参 `[xi, alpha, fx, fy, cx, cy]`（**无单独畸变系数**），分辨率 1920×1080/眼
- cam0=左，cam1=右；cam2 是 pinhole 第三目（非立体对，忽略）
- 基线（cam1 的 `T_cn_cnm1` 平移）≈ **55.7mm**（接近 IPD，立体可用）
- DS 前向投影（照抄 `Tools/undistort_ds.py` 的 `ds_project`，3D 光线→像素）：
  ```
  d1 = |（X,Y,Z）|;  k = xi·d1 + Z;  d2 = √(X²+Y²+k²)
  norm = alpha·d2 + (1-alpha)·k
  u = fx·X/norm + cx;  v = fy·Y/norm + cy
  valid = norm>0 且 Z > -w2·d1        （w2 由 xi,alpha 预计算）
  ```

## 方案

### 1. DS 投影纯数学（可回归单测）——`DoubleSphereProjection`

`ds_project` 逐行照抄成纯 C#（零 UnityEngine），对 Python 原版算出的 **golden 值断言 <0.01px**（中心光线→(cx,cy)、背后光线 valid=0）。shader HLSL 再逐行照抄同一函数——把"DS 投影对不对"从肉眼降为断言（同 VstPassthrough 对 FisheyeProjection 的做法）。

### 2. 标定：yaml → SO（`DsCamchainImporter` + `DsEyeCalibration`）

编辑器 importer 解析 `3-camchain.yaml`（最小解析器，Unity 无 YAML 库）→ 烤成 `RobotDsLeft/Right.asset`（`DsEyeCalibration`：xi/alpha/fx/fy/cx/cy + width/height 1920×1080 + 外参四元数默认单位阵）。**不缩放内参**：shader 用归一化 UV（u/1920, v/1080），960×540 的视频半幅是 1920×1080 的等比降采样，归一化 UV 天然对齐，无需缩放。外参：两目近平行（`T_cn_cnm1` 旋转≈单位，toe<0.3°），v1 用单位阵。

### 3. 显示：DS 穹顶（`RobotDsDome.shader` + `DsDomeRenderer`）

- shader 结构照搬 FisheyeDome（反法线穹顶、单通道立体实例化、边缘/底部羽化、flipV/mirror、SBS UV 分半），**只把 ProjectUV 换成 DS 前向投影**（照抄 `DoubleSphereProjection`）。方向约定：dome 世界系 y-up → 相机系 y-down 在 shader 内 `c.y=-c.y`（投影函数本身保持与 `ds_project` 一致，翻转在外层）；纹理朝向经 flipV 经验校正（视频正立→默认 flipV=1，真机/编辑器实测微调）。
- `DsDomeRenderer` 结构同 FisheyeDomeRenderer，只推 DS uniform（`_LeftIntrin`=(fx,fy,cx,cy)、`_LeftDs`=(xi,alpha,w2,_)、`_ImgSize`=(1920,1080)、UV rect、外参）。新 shader 加入 Always-Included（否则真机剥离→黑屏，见既有 dome-shader 教训）。

### 4. 数据源与 feeder（`RobotDsDomeFeeder`）

- 视频：`VideoPlayer` 播 `StreamingAssets/episode_000000.mp4`（h264，编辑器可解码 → 可肉眼验证；Android 经 streamingAssetsPath URL 播）→ RenderTexture 喂穹顶。SBS 左半→左眼、右半→右眼。
- 复用 VstPassthrough 思路：capture/worldlocked 位姿、`cmd.txt` 调参（radius/latency/mode/flip/mirror/cover/feather/ext/dome/hud/dump）、A 键隐藏穹顶对比原生透视、B 键安全退出、HUD。
- 缺视频优雅降级（日志提示，穹顶空）。

### 5. 场景与构建

- `Editor/RobotDsDomeSceneBuilder`：菜单 `PicoTest/Robot DS Dome/Build Demo Scene` → 生成 `Scenes/RobotDsDomeDemo.unity`（XR Origin + feeder）+ 确保 shader Always-Included。
- `Builder.SceneRegistry` 加 `robotds` → `Build APK/Robot DS Dome` + `Install APK/Robot DS Dome` 自动出现，产物 `PicoTest-RobotDsDome.apk`。Builder 只加一条注册项，不动别的。

## 验收标准

**PC 级（阻塞 commit）**
1. EditMode：`DoubleSphereProjectionTests` 对 Python golden 值 <0.01px；中心=cx/cy；背后 valid=0；`DsCamchainImporter` 解析 yaml 得到正确 xi/alpha/fx/fy/cx/cy/res
2. 全套件保持全绿
3. 编辑器 Play：episode 视频上穹顶去畸变还原（肉眼审，同 RealFisheyeFrameOnDome 流程）

**真机级（设备到位后人审）**
4. 立体左右分眼、转头世界稳定；A 键对原生透视；flip/mirror 经验校正到画面正立自然

## 风险与已知未知

- **DS FOV 与穹顶覆盖角**匹配需实测（alpha≈0.57、xi≈0 → 宽鱼眼，FOV 可能 >190°）；coverageDeg 经 cmd 调。
- **画面朝向**（flipV/mirror）需在真实视频上确认（v1 默认 flipV=1）。
- 外参 v1 单位阵：两目 toe<0.3° 可忽略；无 cam→头外参（camchain 只给 cam 间相对位姿），穹顶各眼采各相机、立体深度来自内容。
- 视频 19MB：大二进制，`episode_000000.mp4` 按需 gitignore（缺失即 feeder 降级 + 相关肉眼审跳过）；yaml/baked SO（纯数字）入库。
- 与 RobotStream(WebRTC) 关系：那个测传输、这个测 DS 光学；将来真机器人 = DS 光学 + WebRTC 传输合流，两实验各验一半。
