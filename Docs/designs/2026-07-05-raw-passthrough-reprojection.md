# raw 复刻 seethrough · 视点重投影 passthrough 设计

状态：**草案（2026-07-05），待人审核**。目标：不调用系统 seethrough，仅用现有 raw 双目鱼眼 + 标定 + 位姿，自建与 seethrough 视觉等效的透视画面。
日期：2026-07-05
工程：`G:\Sdy\ClaudeSdy\PicoTest`（Unity 2022.3.16f1 / URP 14.0.9 / PICO XR / XRI 2.6.4）
前置：`Docs/designs/2026-06-28-fisheye-stereo-dome.md`（本设计是其"固定半径穹顶→深度面"的升级）

---

## 1. 目标与边界

**目标**：戴上 PICO，看到的画面与真实世界**在实用上难以区分**（=系统 seethrough 的效果），但完全由我们从 raw 鱼眼流自建管线产出，不调用 `EnableVideoSeeThrough`。

**为什么不用系统 seethrough**：本项目要采集 raw 鱼眼训机器人，且需要"操作员所见 = 进入训练数据的内容"的可控管线。系统 seethrough 是黑盒、不出深度、不可复用到数据侧。

**根本边界（必须认清，软件补不了）**：
- seethrough 的核心是**逐像素深度 + 视点重投影**。PICO **不开放**它内部用的稠密深度（SDK 只给粗糙 spatial mesh + 平面）。
- 因此"像素级完全一致"不可达。可达目标 = **远中景几乎重合、近景静态对齐良好、近景动态(手)接近但非像素级**。
- 系统 seethrough 在合成器出光时刻重投影（最低延迟）；我们在 App 侧重建**天生慢 ≥1 帧**，必须用 XR 合成层的 timewarp 缓解（M3）。

---

## 2. seethrough 效果分解（要复刻的就是这个函数）

```
眼睛看到的像素 = 重投影( 去畸变(raw), 逐像素深度D, 相机光心C → 眼睛光心E, 出光时刻预测头姿 )
```

四要素与现有条件盘点：

| 要素 | 现有条件 | 状态 |
|---|---|---|
| 内参（去畸变） | `RealLeft/RealRight` fx,fy,cx,cy,k1..k6（1280×960，equiDis62） | ✅ 有 |
| 外参 旋转+**平移** | `VstCamera.GetCameraExtrinsicsfor4U` → 左右 4×4（含基线平移） | ⚠️ 拿得到，当前未用（写死 identity） |
| **逐像素深度 D** | 无稠密深度；只有 spatial mesh（粗、慢）+ 可自算立体视差 | ❌ 需自建（M1/M2） |
| 出光时刻重投影 | OpenXR projection/quad layer + ATW | ❌ 需接合成层（M3） |

**核心洞察**：深度 = ∞ 时，重投影退化为纯旋转 = 现有固定半径穹顶（只有远景对）。把穹顶每像素的"固定半径"换成"真实深度"，穹顶就升级成**深度面**，效果即 seethrough。现有代码不作废，是往上加深度这一维。

---

## 3. 核心几何：视点重投影（本设计的心脏）

对每只眼、每个屏幕像素（等价于深度面网格的每个顶点）：

```
1) 该像素对应一个真实世界点 P（由深度 D 沿相机射线定位）：
     P_world = C_world + D · ray_world        // ray = 相机光心出发的视线方向
2) 相机如何拍到 P（采样用）：
     dir_cam = R_world→cam · (P_world − C_world)
     UV      = 鱼眼正投影(dir_cam, 内参, 畸变)   // 复用 FisheyeProjection
3) 眼睛如何看到 P（渲染用）：
     深度面网格顶点置于 P_world，由 XR 眼相机从眼睛光心 E_world 渲染
```

视差/近景对齐的正确性来源：顶点在**真实 P**，而眼睛 E ≠ 相机 C；从 E 渲染时近点相对远点正确偏移。左眼用左相机+左眼光心，右眼用右相机+右眼光心（双目独立，绝不共用一个中心——这也是当前穹顶视差错的根源之一）。

**可测性（宪法 TDD）**：步骤 2 的采样 = 现有 `FisheyeProjection.ProjectDirection`（已测）。新增的是"相机光心平移 C"与"外参 4×4 → 眼系旋转+平移"的转换，落到 Main.Core 纯 C# 并 EditMode 单测；shader HLSL 逐行照抄，把几何一致性从肉眼降为可断言。

---

## 4. 深度从哪来（现有条件的两条路）

| 路 | 来源 | 覆盖 | 代价 | 里程碑 |
|---|---|---|---|---|
| **B** | PICO spatial mesh 作几何，鱼眼投影贴图 | 静态背景/墙/桌，对齐好 | 便宜、稳；抓不住手/薄/动物体 | M1 |
| **A** | 左右鱼眼实时立体匹配 → 稠密视差 → 深度 | 近景动态、手 | GPU 重、弱纹理噪声、加延迟 | M2 |

现实的 seethrough = A+B 混合 + 硬件加速。我们分阶段：先 B 打底（静态几乎完美），再 A 补近景动态。

---

## 5. 架构（深度面渲染器 = 现有穹顶的升级）

```
VST raw 鱼眼流 (L/R, 1280×960@60)  ──►  _LeftTex/_RightTex
外参 (GetCameraExtrinsicsfor4U)    ──►  FisheyeCalibration(旋转+平移)
深度源 (M1 mesh / M2 视差)          ──►  逐顶点/逐像素深度 D
                                          │
                                          ▼
                    DepthReprojectionRenderer（FisheyeDomeRenderer 升级）
                      - 每眼独立光心 C_L/C_R、眼点 E_L/E_R
                      - 顶点按 D 位移到真实 P；shader 计入光心偏移
                      ▼
                    XR 眼相机渲染 →（M3）进 OpenXR 合成层 timewarp
```

组件（沿用/新增，单一职责）：
| 组件 | 变更 | 里程碑 |
|---|---|---|
| `FisheyeProjection` (Core) | 不变（采样核心，已测） | — |
| `EyeReprojection` (Core, 新) | 光心平移下的方向重投影 + 外参 4×4→眼系转换，纯 C# 可测 | M0 |
| `FisheyeCalibration` | +`extrinsicTranslation`；+从 SDK 外参装配 | M0 |
| `FisheyeDomeRenderer` | +每眼光心偏移 uniform；+深度/半径插桩（M0 常量） | M0 |
| `FisheyeDome.shader` | 射线计入光心偏移；顶点半径可由深度替换 | M0/M1 |
| `VstCameraDomeFeeder` | HeadLocked；运行时外参喂入标定 | M0 |
| `SpatialMeshDepthSource` (新) | 查询 mesh 作深度几何 | M1 |
| `StereoDepthEstimator` (新) | 鱼眼立体匹配→深度纹理 | M2 |
| `PassthroughLayerCompositor` (新) | 结果进 OpenXR 合成层 | M3 |

---

## 6. 里程碑与验收标准

### M0 — 几何地基（无深度，本轮交付，可编辑器验证）
消掉"整体倾斜 / 转头漂移 / 远景不对齐"三类错位，把 raw→显示的**角度管线**校正到位。
- 内容：真实外参旋转（替换 identity）；HeadLocked（实时自视角）；每眼独立光心插桩；coverage/thetaMax 由内参算；平移字段就位（M1 生效）。
- 验收：
  - EditMode：`EyeReprojection` + 外参转换单测全绿（对拍 golden）。
  - 编译通过；`FisheyeDomeXRLive` 场景可构建。
  - **真机（设备门）**：远景(>3m)与真实世界方向重合、整体不倾斜、转头不漂移。

### M1 — spatial mesh 深度面（静态近景对齐）
- 内容：`SpatialMeshDepthSource` 查询 mesh；鱼眼投影贴图到 mesh；从眼视点渲染。
- 验收：编辑器用离线 mesh + 静态鱼眼帧跑通投影贴图（EditMode/PlayMode 烟测）；**真机**静态房间桌面/墙对齐良好。

### M2 — 立体匹配深度（近景动态/手）
- 内容：`StereoDepthEstimator` 鱼眼极线校正→视差→深度纹理→喂深度面。
- 验收：编辑器用一对 golden 立体图验证视差数值（EditMode）；**真机**手部近景对齐、60fps 预算内、噪声可接受。

### M3 — 合成层 timewarp（压延迟）
- 内容：结果进 OpenXR projection/quad layer，ATW 按出光头姿补偿。
- 验收：**真机**快速转头时与静止参照错位显著小于 App 直渲；端到端延迟测量。

> **设备门说明**：M0 的代码/数学可在编辑器全测；M0 视觉、M1/M2/M3 的效果**必须真机验证**（Enterprise 相机需 PICO 4U + 激活，见 journal「pending device」）。编辑器无相机 → 黑屏属预期。

---

## 7. 测试策略（宪法：先测后码）
1. **Core 单测**（EditMode，可断言）：`EyeReprojection` 光心平移重投影；外参 4×4→旋转+平移转换；边界（深度→∞ 退化为纯旋转、near-axis 无 NaN）。
2. **shader 一致性**：HLSL 逐行照抄 Core，取样方向对拍（沿用 `FisheyeProjectionTests` 模式）。
3. **PlayMode 烟测**：深度面渲染器装配不报错、参数推送正确（沿用 `FisheyeDomeRenderSmokeTests`）。
4. **真机 AutoTest**（设备到位）：远景重合、转头稳定、近景对齐、延迟测量。

---

## 8. 风险与边界
- **深度质量是天花板**：mesh 粗、立体匹配噪声 → 近景动态非像素级。认清"接近而非完全一致"。
- **延迟**：App 侧重建慢 ≥1 帧，必须 M3 合成层补偿，否则转头漂移。
- **外参坐标系**：`GetCameraExtrinsicsfor4U` 的手性/基准需真机核对（PICO Unity 插件多已转 Unity 左手系，但不假设，M0 留验证点）。
- **性能**：M2 稠密鱼眼立体在移动 GPU 是硬约束，需降分辨率 + 时域滤波，可能限制 fps。
- **相机共存**：本方案全程用 raw 采集，不开系统 seethrough，无共存冲突（这是选 raw 路线的附带好处）。

---

## 9. 决策（本设计提出，待人审核）
- [ ] 采用"深度面 = 固定穹顶升级"架构，而非另起炉灶。
- [ ] 深度分阶段：M1 spatial mesh 打底 → M2 立体匹配补近景。
- [ ] 实时自视角用 HeadLocked（区别于遥操作的 WorldLocked）。
- [ ] 承诺线：远中景≈seethrough、近景静态对齐、近景动态接近；**不承诺像素级完全一致**（深度不开放的硬边界）。
