# M2 设计：数据管线 + 操控台（已批准）

状态：用户已批准（2026-06-06，方案 A）。参考项目：`G:\Sdy\ClaudeSdy\YC-Ego`（已跑通的 PICO 4U ego 采集应用，借鉴其时间基准/相机流/verify 工具约定）。

## 目标

无设备条件下建成完整可测的采集数据链路：**Mock 采集源 → 录制 → 分段落盘 → 上传 → Node 接收服务**，配 **PC 浏览器操控台**（连 RemoteBridge）。真实采集源（M4）只替换 `ICaptureSource` 实现，链路不动。

## 架构（方案 A）

```
[ICaptureSource]──帧──▶[SessionRecorder]──▶[ChunkWriter 分段落盘]──▶[UploadQueue]──▶[Node 接收服务]
 Mock关节/Mock视频          (Main.Core 纯C#)        CaptureData/<sid>/         分块+续传      Server/ + CaptureData
                                  ▲
[操控台网页(单文件HTML)] ◀─HTTP 2Hz 轮询/命令─ [RemoteBridge HttpListener :8090]（仅 Dev 构建）
```

- 采集/录制/上传核心全在 `Main.Core`（零 UnityEngine，EditMode 秒级测试）；MonoBehaviour 薄壳只做时钟驱动、persistentDataPath、场景装配
- 后端 Node.js + Express（复用已有 Node 依赖），~300 行
- RemoteBridge 纯 HTTP（IL2CPP/Android 最稳；WS 留 M3 按需升级）

## 1. 数据 Schema（Main.Core/Schema）

- `SessionMeta`：sessionId(GUID)、startedAtUtc、deviceInfo、schemaVersion(=CoreInfo.SchemaVersion)、streams[]、tags[]、**timeBase**（单调时钟起点 + BOOTTIME 锚点字段，对齐 YC-Ego 约定，PC 上锚点置 0）
- 时间戳：`timestampNs`（long，Stopwatch 单调纳秒，线程安全）
- 流类型：`BodyPose | Video | PointCloud`（M2 实现前两种 Mock）
- 关节帧：24 骨骼点 ×（Vec3f 位置 + Quatf 旋转），对齐 PICO Body Tracking 布局；**Core 自定义 blittable Vec3/Quat**（禁 UnityEngine）
- 视频帧（M2 Mock）：timestampNs + frameIndex + width/height + payloadSize + crc32 + 合成载荷；真实视频 M4 按 YC-Ego 模式（HEVC mp4 + index.bin），Schema 预留 `externalContainer` 字段
- 帧率：关节 72Hz、视频 30fps

## 2. 录制与存储（Main.Core/Recording）

- `chunk_NNNN.ptc`：头（magic `PTCH`+版本+sessionId+streamId）+ 长度前缀帧序列；**64MB 或 60s 滚动**（崩溃只丢段尾）
- `manifest.json` 每段关闭增量更新，停止时终化；启动扫描未终化会话→从段重建（崩溃恢复）
- 写盘在独立线程（YC-Ego 红线：IO 不进主线程）；环形队列缓冲，满则计 droppedFrames
- 磁盘守卫：<500MB 自动停录
- 目录：`CaptureData/<sessionId>/manifest.json + streams/<流>/chunk_*.ptc`

## 3. 上传契约（Server/openapi.yaml 为唯一契约）

| 端点 | 作用 |
|---|---|
| POST /api/v1/sessions | 注册会话（manifest）|
| HEAD/PUT /api/v1/sessions/{id}/files/{path}（offset 参数） | 分块上传 + 断点续传 |
| POST /api/v1/sessions/{id}/complete | 校验和清单 → 服务端校验落定 |
| GET /health | 探活 |

- 采集期间不上传；停止后 UploadQueue 按段上传，指数退避重试
- 鉴权：`X-Api-Token` 头（env 配置占位）
- 服务端数据落 `Server/data/`

## 4. RemoteBridge（Main/Scripts/RemoteBridge，仅 Development 构建）

HTTP :8090（M2 绑 localhost；M3 LAN+token）：
- `GET /bridge/state`：录制状态/帧率/丢帧/存储/XR 状态
- `POST /bridge/command`：capture.start / capture.stop / session.tag / ui.click / app.quit
- `GET /bridge/sample?stream=&count=`：样本帧（AI 验数值）
- `GET /bridge/screenshot`：眼内画面 PNG
- `GET /bridge/ui/list`：可点控件枚举

三个消费者：操控台 / AI 探索实验 / AutoTest(M3)。

## 5. 操控台（Server/console/index.html，单文件零构建）

- Node 托管 `/console`；Bridge 地址可配
- 面板：会话控制（开始/停止/打标）、实时状态（帧率/丢帧/时长/存储/红点）、上传进度、事件日志
- 样式由 `Docs/designs/ui/design-tokens.json` 生成 CSS 变量（UI 三段式管线第一次落地；同一 tokens 后续生成 uGUI 主题）
- 设计稿先出 HTML 原型给用户浏览器审核，批准后才接真 Bridge

## 6. VR 内状态面板（操控台验收后）

世界空间 uGUI：录制红点+帧率+存储；样式只引用 tokens 生成的主题资产（宪法 #13）；XR Device Simulator 截图验收。

## 7. 测试策略

- **EditMode（Core）**：序列化往返、chunk golden 字节、截断恢复、上传重试/续传（内存假服务器）、时间戳单调、丢帧计数
- **PlayMode**：Mock 全链路（开会话→录2s→停→传到进程内 C# 假服务器→断言收齐）
- **e2e** `Tools/run-e2e-local.ps1`：起真 Node → RPC 驱动 PlayMode 场景跑链路 → 服务端断言（=“PC 级后端对接调试”自动化）+ PC 端 verify 脚本（借鉴 YC-Ego `verify_session.py` 统一 PASS/FAIL 风格）

## 8. 错误处理

录制器异常不抛入 Unity 主循环（会话标 failed+日志）；上传失败退避重试不阻塞新会话；一切错误经 /bridge/state 可见。

## 非目标（YAGNI）

真实采集源、视频编码、WebSocket、正式鉴权、点云、完整 uGUI 主题生成器（只做 tokens→CSS + 最小主题资产）。

## YC-Ego 借鉴清单（M4 重点参考）

- TimeBase 单调 ns + BOOTTIME 锚点（已纳入本设计 manifest）
- 相机流：HEVC mp4 + camera_index.bin 1:1 对齐（M4 直接照此）
- Enterprise 激活/装机/批权限流程：`YC-Ego/docs/onboarding.md`
- PICO SDK API 字典：`YC-Ego/docs/pico-sdk-reference.md`（不发明 API）
- PC verify 工具链模式：`YC-Ego/tools/verify_*.py`
