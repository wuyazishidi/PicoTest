# 2026-06-06 | M2a 数据管线（子代理驱动执行完成）

## 交付（分支 m2a-data-pipeline，12 任务全过两段审查）

完整可测采集链路 + **e2e 实证通过**（`Tools\run-e2e-local.ps1` → "E2E PASS (client + server verified)"）：

- `Main.Core/Schema`：Vec3f/Quatf/StreamType（显式小端零分配序列化）、BodyPoseFrame(24关节/680B)、VideoFrameMeta、Crc32（与 Node 端互操作验证）
- `Main.Core/Recording`：ChunkWriter/Reader（PTCH 容器、64MB 滚段、截断容错）、Manifest（File.Replace 原子写、损坏容错恢复→.corrupt）、SessionRecorder（每流写线程+有界队列+丢帧计数、IChunkSink 注入缝、故障→Failed+FirstFault）
- `Main.Core/Capture`：ICaptureSource（Tick 泵送确定性）、Mock 关节/视频源、CaptureSession（重启安全、快照含终化统计+FaultMessage）、IClock/MonotonicClock
- `Main.Core/Transport`：IHttpTransport、UploadQueue（HEAD 续传+PUT 分块+统一重试预算+Failed 会话拒绝）、HttpClientTransport（HEAD Content-Length→Body 对齐）
- `Server/`：Node+Express 接收服务（openapi.yaml 契约、断点续传、CRC 校验落定 .complete、**SessionId 校验+resolve 包含性防穿越**）、node --test 6 个
- `Assets/Editor/E2E` + `Tools/run-e2e-local.ps1`：一键全链路验收

测试：EditMode 46 + PlayMode 1 + node 6 = **53 个全绿**。

## 审查拦下的真问题（两段审查制的价值实证）

1. SessionRecorder 写线程异常被吞 + 损坏会话错标 Completed（BLOCKER，违设计 §8）
2. Stop/Enqueue 竞态：CompleteAdding 后 TryAdd 抛异常进采集线程（MAJOR）
3. Manifest Delete+Move 非原子（崩溃窗口丢 manifest）+ 恢复函数遇损坏 manifest 反而抛异常（MAJOR×2）
4. Node 服务 SessionId 路径穿越（BLOCKER）+ 黑名单式防御缺陷（MAJOR）
5. 反序列化无边界校验、上传重试预算混乱等

## 遗留（非阻塞，记录在案）

- SessionRecorder：故障后丢弃的帧不计数（MINOR）；GetFrameCount 对未知流抛 KeyNotFound（与 Enqueue 风格不一致）
- Mock 源缺三条语义注释（Tick 爆发补帧/订阅者异常/分配）
- FakeIngestServer 与 Node 的 HEAD 语义差异靠 Task10 适配层对齐（已注释）
- C 盘空间告急（执行中曾满到 0 → 测试抖动）：**需用户清理磁盘**
- 设计 §2 磁盘守卫（<500MB 停录）随 MonoBehaviour 薄壳层落地（M2b）；§3 服务端 X-Api-Token 校验留 M3（客户端已发头，server 端 TODO 已知）—— 终审确认非阻塞

## 环境教训（新增）

- Tests.EditMode.asmdef 的 overrideReferences=true 时 Newtonsoft 须显式加 precompiledReferences
- express.raw 的 `type:'*/*'` 不匹配无 Content-Type 请求 → 用 `type:()=>true`
- AssertConsoleContains 参数名 = `keyword`
