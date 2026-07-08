# VST Passthrough Demo（Exp-VstPassthrough）

日期：2026-07-08　分支：`fisheye-stereo-dome`　状态：实验（未晋升）

## 目标

新建 Demo 优化 `FisheyeDomeXRLive`（XRLiveDemo），**优化目标 = 用自有"VST raw 鱼眼 → 穹顶"管线复现 PICO 原生透视（see-through）的效果**：画面与真实世界对齐（1:1 尺度、正确朝向、转头不漂移），能与系统原生透视 A/B 对比逼近。

这是遥操作管线的正确性验收手段：如果我们能用头显自带双目鱼眼 + 出厂标定还原出"像原生透视一样"的画面，同一管线换成机器人相机流就是可信的。

## 与 XRLiveDemo 的差距（现状分析）

| 维度 | FisheyeDomeXRLive 现状 | 原生透视 | 本 Demo 方案 |
|---|---|---|---|
| 锚定 | WorldLocked + 云台伺服（遥操作用） | 相机刚性随头 | 头锁定（去掉伺服） |
| 外参 | 单位阵（journal 2026-06-30 遗留项） | 标定外参重投影 | `T_imu_to_cam` 换算进 shader `_LeftRot/_RightRot` |
| 延迟 | 不补偿（画面滞后随头拖动 → 游移） | 捕获时刻位姿重投影 | 穹顶锚到"捕获时刻头位姿"（环形缓冲回溯，延迟可调） |
| 尺度 | radius=20m（远景） | ~1m 级重投影面 | radius 缺省 1.5m，真机可调 |
| 调参 | 改码重打包 | — | adb cmd.txt 运行时调参 + A 键切原生透视对比 |

## 方案

### 1. 外参换算（纯数学，可单测）——`ImuCamRig`

`cam_calib.json` 每眼有 `T_imu_to_cam`（OpenCV 约定：p_cam = R·p_imu + t，相机系 x 右/y 下/z 前）。IMU 系轴向约定未知 → **不臆测，从标定自标出头系基**：

- 相机中心（IMU 系）`p = -Rᵀt`；基线向量 `pL−pR` 模长应复原 `stereo_baseline_m=0.064`
- 头系基（IMU 坐标表示）：`Z=normalize(fL+fR)`（两相机光轴均值 = 前），`X=normalize(pR−pL)` 去 Z 分量（左→右相机 = 用户右），`Y=Z×X`（右手系 = 上）
- Unity 头系方向 `(dx,dy,dz)`（右/上/前）→ IMU 向量 `dx·X+dy·Y+dz·Z` → shader 每眼旋转 **`R_eye = R_imu_to_cam · M`**（M 列 = X,Y,Z），恰为 shader `c = mul(R, d)` 需要的"头系方向→相机系"
- 同时输出相机在头系的位置（左 x≈−0.032 / 右 +0.032），v1 仅用于测试断言，平移补偿留后续

### 2. 运行时组件——`VstPassthroughFeeder`

复用 `Main.Vst.VstCamera`（帧泵/崩溃规避/暂停恢复照抄 XRLive feeder），差异：

- **标定来源**：运行时读 `StreamingAssets/cam_calib.json`（Android 走 UnityWebRequest，编辑器直接读文件）→ `CamCalib.Parse` → 动态建 `FisheyeCalibration`（含 `extrinsicRotation`＝R_eye 四元数）。读不到 → 回退 Inspector 指的 RealLeft/RealRight + 单位阵外参，并 HUD/日志警告。
- **两种锚定模式**（运行时切换）：
  - `head`：每帧穹顶位姿 = 当前头位姿（朴素头锁定，基线对照）
  - `capture`（缺省）：每帧记录头位姿进环形缓冲；新帧到达时穹顶位姿 = `now − latencyMs` 时刻的头位姿 → 捕获后转头画面世界稳定（近似 late-stage reprojection，原生透视稳定感的来源）
- **A/B 对比**：右手 A 键（primaryButton）或 cmd `dome off` 隐藏穹顶 → 露出系统原生透视；再按恢复。优化即"切换时看不出跳变"。
- **adb 调参通道**（照抄 Exp-TrackerIMU cmd.txt 模式，1s 轮询 `persistentDataPath/passthrough/cmd.txt`）：`radius <m>` / `latency <ms>` / `mode head|capture` / `ext calib|id` / `dome on|off` / `cover <deg>` / `feather <deg>` / `hud on|off` / `dump`
- HUD（TextMesh，跟头 2m）显示模式/radius/latency/帧率；透视开启、B 键退出（killProcess 路径）照抄 XRLive feeder。

### 3. 场景与构建

- `Editor/VstPassthroughSceneBuilder`：菜单 `PicoTest/Build VST Passthrough Demo Scene` → 生成 `Assets/Experiments/Exp-VstPassthrough/Scenes/VstPassthroughDemo.unity`（XR Origin + feeder，极简，宪法 #14）
- `Editor` 菜单 `PicoTest/Build VST Passthrough (in-editor)` → Release APK `Builds/PicoTest-VstPassthrough.apk`（Release 规避 CheckJNI 崩溃，同 Builder 现约定）
- 无新 shader（`PicoTest/FisheyeDome` 的 `_LeftRot/_RightRot` 位已支持外参），Always-Included 已配置

## 验收标准

**PC 级（阻塞 commit）**
1. EditMode：ImuCamRig 用真实 cam_calib.json —— 基线复原 0.064±0.001m；R_eye 近单位阵：三个头系基方向各映射到自身附近（>0.95。注：现管线 identity+flipV=1 实测画面正立 ⇒ 有效相机系为 **y-up**，故 R_eye 末端做 diag(1,−1,1) 翻转，头上方→相机 +y）；左右相机头系位置 x≈∓0.032；R_eye 正交且 det=+1
2. 全套件（EditMode+PlayMode）保持全绿

**真机级（设备到位后人审）**
3. `capture` 模式下转头，穹顶画面世界稳定不拖影（对比 `head` 模式的游移）
4. A 键切原生透视对比：手/桌面等近景位置偏差肉眼可辨程度 ≤ 数厘米级，尺度无明显缩放
5. `ext id` vs `ext calib` 对比可见对齐改善（证明外参换算方向正确）

## 风险与已知未知

- **（实现期发现，已用测试锁定）`T_imu_to_cam` 字段名与数据语义相反**：按字段名 imu→cam 解读时基线方向（x̂_imu）与图像横轴（−ŷ_imu）垂直、物理矛盾；按 cam→imu（相机位姿）解读则基线∥图像横轴、光轴朝前，完全自洽。ImuCamRig 对两种解读做共线性打分自动选择（真实数据 score≈1.0 vs ≈0），不信字段名
- IMU 系轴向约定虽自标，但**图像流方向 vs 标定坐标**可能有 90°/镜像出入（VST 服务可能旋转过输出）→ 真机用 `ext id|calib` + flip 对比定位；这正是留 adb 调参通道的原因
- 旋转-only 近似：相机与眼位置差（数 cm）在近距离产生视差误差，radius 调参只能折中，非本 v1 消灭目标
- frame.timestamp 基准（SDK ns）与 Unity 时钟换算未知 → v1 用固定 latencyMs 近似，真机调
