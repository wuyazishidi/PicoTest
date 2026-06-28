# 双目鱼眼遥操作 · 球面投影设计（具身机器人视角）

状态：**4 项关键决策已定（2026-06-28），用户指示进入计划阶段**。可执行计划见 `Docs/plans/2026-06-28-fisheye-stereo-dome.md`。
日期：2026-06-28
工程：`G:\Sdy\ClaudeSdy\PicoTest`（Unity 2022.3.16f1 / URP 14.0.9 / PICO XR / XRI 2.6.4）

---

## 1. 目标与原则

把机器人**双目鱼眼相机**的画面投影到左右眼各自的**球面/穹顶**上，让"我就是机器人的眼睛"——
即 **具身一致性（embodiment）**：机器人相机在世界方向 θ 拍到的光线，必须以同样的方向 θ 呈现到我的眼睛。

三条一致性保证：
1. **角度 1:1**——shader 用鱼眼内参+畸变做**反投影**（视线方向 → 像素），物体角直径/方位由标定决定，无需手调大小/距离。
2. **双目视差给深度**——左相机→左眼、右相机→右眼，**保留两图视差**，大脑融合深度。绝不把两图合并到同一张。
3. **本地转头瞬时响应 + 云台慢伺服扩展视野**（混合，见 §6）——转头在已收到的 220° 画面内本地环顾（零新增延迟），云台低速跟到平均注视方向保持居中并能看向背后。

### 验收标准（用户最终要的效果 → 由哪条保证 → 硬依赖）
| 目标 | 保证 | 硬依赖（不满足则拿不到） |
|---|---|---|
| **沉浸感** | 220° 穹顶填满视野 + 本地转头瞬时响应 | 端到端延迟低；转头走本地 reprojection |
| **3D 效果** | 双目视差（保证 2） | **机器人基线 ≈ 人眼 IPD(~63mm)**；左右外参对齐无垂直错位 |
| **画面与机器人同步** | 混合转向（保证 3） | 运动到光子延迟可控；云台慢伺服不追头部微动 |
| **1:1 还原** | 鱼眼反投影（保证 1） | **鱼眼标定精度**（fx,fy,cx,cy,k1–k4 准确） |

> **根本边界（必须认清）**：本方案=两张纹理贴无穷远穹顶，给的是**转头方向 1:1 + 双目深度**，**不给平移视差**——前倾/侧身看物体侧面时画面不变。真正的 6DoF 平移还原需体积重建/光场，属另一量级工程，不在本设计范围。承诺线：**转头 = 1:1 + 3D；探身平移 = 不还原**。

**方案选定：A（鱼眼直接投球，一次采样）。** 不走"去畸变成平面→弧形UI→手调大小距离"（会丢广角、二次重采样、角度只能近似）。
CurvedUI 仅作"网格弯曲 + 朝里看"概念参考，**球面自写代码**，不依赖其运行时。

---

## 2. 总体架构

```
机器人双目鱼眼相机
   │ (传输/解码：本设计范围外，约定产出两张纹理)
   ▼
_LeftTex / _RightTex (RenderTexture/Texture2D，左右各一)
   │
   ▼
FisheyeDomeRenderer (MonoBehaviour)
   - 持有：左右内参/畸变/外参、图像尺寸、FOV上限、渲染坐标系模式
   - 每帧把参数推给材质 (MaterialPropertyBlock)
   ▼
InvertedSphere (反法线球网格) + FisheyeDome.shader (URP, 立体实例化)
   - 每片元：视线方向 → (外参旋转) → 鱼眼正投影 → UV → 采样对应眼纹理
   ▼
锁定到 RobotHeadAnchor (世界/机器人头坐标系，可配置) → 头显左右眼看到具身画面
```

### 组件清单（每个单一职责）
| 组件 | 类型 | 职责 | 依赖 |
|---|---|---|---|
| `FisheyeProjection` | Main.Core 纯 C# | §3 鱼眼正投影数学（方向→UV），shader 的可测镜像 | 无 |
| `FisheyeCalibration` | ScriptableObject | 存一只眼的 fx,fy,cx,cy,k1–k4,宽,高,外参旋转 | Main.Core 数据 |
| `FisheyeDomeRenderer` | MonoBehaviour | 把左右标定+纹理推到材质；管理渲染坐标系 | 材质、网格 |
| `InvertedSphereMesh` | static util | 运行时生成反法线球/穹顶网格 | 无 |
| `FisheyeDome.shader` | URP unlit shader | 方向→鱼眼UV→采样，立体实例化，FOV裁剪 | 无 |
| `RobotHeadPoseDriver`（可选） | MonoBehaviour | 用机器人头位姿遥测驱动 RobotHeadAnchor | 遥测源 |

---

## 3. 鱼眼数学（shader 核心，OpenCV fisheye 正投影）

给定**眼坐标系**下的视线方向 `d = (dx,dy,dz)`（已归一化，+Z 为光轴前方）：

```
// 1) 先用外参把方向转到“该眼物理相机坐标系”，做极线对齐/校正横滚
d_cam = R_eye * d            // R_eye 来自相机外参（每眼一个 3x3）

// 2) 离轴角 theta（对宽FOV鲁棒，用角度而非除以Z）
theta = atan2( length(d_cam.xy), d_cam.z )    // 0..(FOV/2)
phi_dir = normalize(d_cam.xy)                 // (cosφ, sinφ)

// 3) 等距畸变（k1..k4）
theta2 = theta*theta
theta_d = theta * (1 + k1*theta2 + k2*theta2^2 + k3*theta2^3 + k4*theta2^4)

// 4) 归一化像面 → 像素 → UV
x = theta_d * phi_dir.x
y = theta_d * phi_dir.y
u = fx*x + cx
v = fy*y + cy
uv = float2(u / width, v / height)            // 注意 v 是否翻转，按纹理朝向定

// 5) FOV 裁剪：theta > thetaMax 的片元 → 黑/羽化（超出相机视锥）
```
数值守护：`theta→0` 时 `phi_dir` 退化，用 `length(xy) < eps` 分支置 0；`d_cam.z<0`（>180°鱼眼）仍由 `atan2` 正确给出 `theta>π/2`。

> 反投影一致性：因为我们对"每条视线方向"求其鱼眼像素，等价于 OpenCV `fisheye::projectPoints` 的逐方向求值，角度天然 1:1。可离线用 cv::fisheye 抽样几个方向比对验证（见 §8）。
>
> **可测性关键**：§3 公式先用纯 C# `FisheyeProjection`（Main.Core）实现并 EditMode 单测（对 OpenCV golden 值 <1px），shader HLSL 逐行照抄同一公式——把"角度 1:1"从肉眼判断降为可断言的回归。

---

## 4. 立体渲染（URP 单通道实例化）

- 材质同时持有左右两套参数与两张纹理：`_LeftTex/_RightTex`、`_LeftIntrin/_RightIntrin(fx,fy,cx,cy)`、`_LeftDist/_RightDist(k1..k4)`、`_LeftRot/_RightRot(3x3)`、`_ImgSize`、`_ThetaMax`。
- shader 用 `UNITY_VERTEX_OUTPUT_STEREO` + `unity_StereoEyeIndex` 选择左/右参数与纹理；开启 `multi_compile` 立体实例化宏。
- 一个**反法线球**网格（大半径，居中于头部），左右眼各自采样自己纹理——视差来自纹理内容，几何半径无所谓（当作无穷远背景）。
- 渲染为背景：`ZWrite Off`、`ZTest LEqual`/或 Background 队列、`Cull Front`（看球内壁）。

---

## 5. 球/穹顶网格（自写）

- `InvertedSphereMesh.Create(coverageDeg, segments)`：生成 UV 球，**法线翻转朝内**；可只生成覆盖 FOV 的穹顶（如 220°）以省片元。
- 居中于头部（作为 XR 相机/Anchor 子物体放在眼点附近），半径取 10–50m（仅作方向载体）。
- 借鉴 CurvedUI 的只是"朝内看的弯曲网格"理念；网格与采样全自写。

---

## 6. 渲染坐标系（具身一致性的关键，可配置）

`FisheyeDomeRenderer.RenderFrame` 枚举：
- **WorldLocked（默认主体，已定）**：穹顶挂在 `RobotHeadAnchor`（机器人头/世界坐标系），**不随头转**。你转头时 XR 相机在静止的 220° 穹顶内环顾——**零新增延迟、瞬时响应**，沉浸与同步感最佳。视野上限=相机已传的 220°。
- **HeadLocked（备选）**：穹顶挂在 XR 相机下随头转，适合相机视场窄、必须靠云台实时追头的场景。转头走全链路，延迟敏感。

> ✅ 决策（2026-06-28）：采用**混合转向** = WorldLocked 主体 + 低速率云台伺服。
> - **转头（快）**：本地在 220° 穹顶内环顾，瞬时，不触发任何回传链路 → 沉浸+同步的关键。
> - **云台（慢）**：`RobotHeadPoseDriver` 以**低速率**把云台伺服到你的**平均注视方向**（非追每次头部微动），用途仅两点：①保持画面居中、②让你能转向相机 220° 之外（如正后方）。低速率 ⇒ 不给最敏感的转头动作引入延迟。
>
> ⚠️ 残余延迟缓解：云台慢伺服跟随期间，用本地 IMU 预测头朝向在穹顶上做**预测性 reprojection** 补偿——这正是取 220° 而非窄 FOV 穹顶的价值（留出 reprojection 余量）。留作联调阶段优化，不阻塞首版。
> 实现注解：`RobotHeadPoseDriver` 的伺服速率/死区设为可配参数；首版可先纯 WorldLocked（云台不动）跑通，再接低速伺服。

---

## 7. 数据流与外部约定

- 输入：约定上游把左右鱼眼解码进 `_LeftTex/_RightTex`（传输/解码 RTSP/WebRTC/UVC 属本设计范围外）。
- 标定：`FisheyeCalibration` 资产（左右各一），含 fx,fy,cx,cy,k1–k4,width,height,外参旋转（**相对机器人头**，见 §11 决策 4）。由你提供。
- 输出：左右眼各自看到具身画面。
- **待你提供的两个硬参数**（不阻塞写代码，阻塞"真 1:1/真 3D"验收）：①机器人双相机**基线 mm**（决定 3D 深度是否 1:1）；②**鱼眼标定来源**（OpenCV `cv::fisheye::calibrate` / 厂商给）。拿到后填入 §7 与示例标定资产。

---

## 8. 验证与测试（角度一致性如何证明）

1. **离线/单测比对**：取 N 个视线方向，用 OpenCV `cv::fisheye::projectPoints` 算像素 golden，与 `FisheyeProjection`（C#）+ shader 公式对拍，误差 < 1px（EditMode 可断言）。
2. **直线检验**：在机器人前放已知直边（门框/标定格），正确反投影下扫视时直边仍直、角宽合理。
3. **基线/IPD**：确认左右纹理视差方向正确（近物体在左右眼水平错开，能融合不重影）；机器人基线≈IPD 最佳。
4. **FOV 裁剪**：超出 thetaMax 的区域应为黑/羽化，不应出现拉伸鬼影。
5. **晕动检查**：左右外参未对齐会上下错位→久看晕；用 §3 的 `R_eye` 校正后复查。

---

## 9. 边界与风险
**地基级（决定四个目标能否拿到，软件补不了）：**
- **1:1 还原 ⟸ 标定精度**：fx,fy,cx,cy,k1–k4 不准 → 角度非 1:1，物体偏大/小、直边变弯。标定是整个效果的地基（§7 由你提供）。
- **3D 效果 ⟸ 机器人基线 ≈ 人眼 IPD(~63mm)**：基线远大于 IPD → "巨人视觉/微缩世界"，深度感错且晕；这是机器人硬件约束，shader 修不了。基线 ≠ IPD 时只能近似缩放，无法真正 1:1 深度。
- **沉浸+同步 ⟸ 端到端延迟**：混合方案已把最敏感的转头放到本地（瞬时）；剩余链路（云台慢伺服+视频回传）延迟仍需控，否则世界变化滞后。
- **根本边界**：无平移视差（见 §1）——探身/侧移看物体侧面不还原；需要时另案做 6DoF 体积重建。

**实现级：**
- 纹理朝向/翻转（v 轴、左右镜像）需按实际流核对，预留 `_FlipV/_Mirror` 开关（默认 0/0，sRGB）。
- 色彩空间：相机流多为 sRGB，URP 线性工作流下注意纹理 sRGB 标记。
- 单通道实例化宏齐全，否则右眼采样错误。
- 极端鱼眼（>200°）边缘畸变系数外推不稳→用 thetaMax 裁掉无效边缘。
- 性能：近全屏片元 + 几次多项式，移动 XR 可接受；穹顶限 220° FOV 进一步省。

---

## 10. 实施步骤（落地顺序）
落地拆解见 `Docs/plans/2026-06-28-fisheye-stereo-dome.md`（按 TDD 任务化）。概要顺序：
1. `FisheyeProjection`（Main.Core 纯 C# 数学）+ EditMode 单测（对 OpenCV golden）。
2. `FisheyeCalibration`（ScriptableObject）+ 一份示例资产。
3. `InvertedSphereMesh` 网格生成 + EditMode 几何单测。
4. `FisheyeDome.shader`（URP unlit，立体实例化，§3 数学，FOV 裁剪，翻转开关）。
5. `FisheyeDomeRenderer`：参数/纹理推送（MaterialPropertyBlock）、RenderFrame 切换。
6. 测试场景：两张静态鱼眼样图喂入，肉眼+离线比对验证。
7. （可选）`RobotHeadPoseDriver` 接遥测。
8. 接真实双目流联调（传输层另案）。

---

## 11. 决策（已定 2026-06-28）
- [x] 转向模型：**混合** = `RenderFrame` 主体 **WorldLocked**（转头本地瞬时环顾 220° 穹顶）+ **低速率云台伺服**（`RobotHeadPoseDriver` 跟到平均注视方向，保居中、可看背后）。首版可先纯 WorldLocked 跑通再接慢伺服（见 §6）。
- [x] 穹顶覆盖角：**220° 穹顶**（非全球）→ `InvertedSphereMesh.Create(220°, segments)`，配合 shader `_ThetaMax` 裁边；为慢伺服期的预测性 reprojection 留余量。
- [x] 纹理来源格式与朝向：**sRGB / 左右各一张 / 不翻转不镜像** → 默认 `_FlipV=0 _Mirror=0`，左右纹理标记 sRGB（URP 线性工作流自动转换）；开关保留供联调微调。
- [x] 左右外参提供形式：**相对机器人头** → `FisheyeCalibration.extrinsicRot = R(eye→robotHead)`，左右各一 3×3，shader 中即 §3 的 `R_eye`。
