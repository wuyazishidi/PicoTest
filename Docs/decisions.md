# 技术决策记录

格式：日期 | 决策 | 理由 | 影响

## 2026-06-06 | 基线决策（M0）

| 决策 | 理由 |
|---|---|
| Unity 2022.3.16f1 不升级 | 既有环境；官方 MCP（需 Unity 6）放弃，换 YIUIMCP |
| YIUIMCP (commit 8c61809) 作为 AI↔Unity 编排层 | CLI-first、Domain Reload 恢复、支持 2022.3；上游无 asmdef，自补 `cn.etetet.yiuimcp.Editor.asmdef` + `UTO/tsconfig.json`（升级上游时保留这两个文件） |
| Odin Inspector 本地保留、不入库 | YIUIMCP 硬依赖（基类用 Odin 特性）；付费许可禁止再分发 |
| 仓库禁用全局 gitignore | `~/Documents/gitignore_global.txt` 含 `Assets/**/*.meta`、`*.dll` 等对 Unity 致命的规则；本仓库 excludesFile 指向空文件 |
| 测试结果走文件中转（Logs/TestResults/latest.json） | PlayMode 测试触发 Domain Reload 会杀掉 RPC 返回通道；[InitializeOnLoad] 持久回调 + 轮询是唯一可靠方案 |
| 渲染管线 = URP（M1 落地） | PICO 样例主流、透视/MR 需要 |
| PICO Integration SDK 3.4.0（本地 G:\Unity\PICO-Unity-Integration-SDK，commit c93a59b / release_3.3.0+5） | 最新稳定；min Unity 2021.3；manifest 用 `file:` 引用（293MB 不入库），换机器需先 clone SDK 到同路径或改 manifest |
| 包版本钉死：XRI 2.6.4 / XR Mgmt 4.4.0 / URP 14.0.9 / InputSystem 1.7.0 | 与 2022.3.16f1 兼容的保守组合；升级走实验+回归流程 |
| 数据 Schema 版本常量 `CoreInfo.SchemaVersion` | 采集数据格式演进的兼容性锚点 |

## 版本独立性说明

`ChunkWriter.FormatVersion`（chunk 文件容器格式）与 `CoreInfo.SchemaVersion`（帧内容 schema）相互独立，各自递增。容器格式升级（如头部字段变更）不影响帧内容版本；帧内 schema 演进（如新增骨骼字段）不触发容器版本升级。

## 2026-07-02 | WebRTC 机器人双目鱼眼接收（Exp-WebRTC）

| 决策 | 理由 |
|---|---|
| WebRTC 技术栈 = shiguredo/webrtc-build（预编译 libwebrtc）+ 自写 C API wrapper（`extern "C"`，libyuv 做 I420→RGBA）+ Unity P/Invoke | 用户指定；预编译免自编 libwebrtc；C wrapper 隔离 C++ 符号供 P/Invoke |
| **首个原生插件**：.so 入 `Assets/Plugins/Android/libs/arm64-v8a/`，`WEBRTC_NATIVE` 宏门控编入 | 此前项目无 P/Invoke；宏门控使缺库时默认走假帧源、不破坏构建 |
| 显示/云台/透视/退出复用 FisheyeDomeXRLive，仅换数据源 | 探索确认 dome 管线源无关；feeder 只需产出 RGBA32 SBS 纹理 |
| 视频格式 = 双目鱼眼 SBS 2560×720（每眼 1280×720） | 机器人相机（用户定）；dome 与分辨率无关，仅需对应标定 + SBS 分半 |
| 信令首版 = 自定义 JSON/WebSocket（`ISignaling` 可换 HTTP/Ayame） | 现有 IHttpTransport 无 WebSocket；本地易搭；抽象留后路 |
| 新增 Android 权限 INTERNET / ACCESS_NETWORK_STATE | WebRTC 信令 + 媒体传输需联网 |
| 落位 `Assets/Experiments/Exp-WebRTC/`，验证后晋升 | 宪法：原生集成风险高，先实验隔离 |

> 遗留：机器人相机鱼眼标定（每眼 1280×720）为外部依赖；shiguredo 版本/体积/授权；编解码硬解可用性。见 `.claude/plans/2026-07-02-webrtc-stereo-dome.md`。

## 待决（需要外部输入）

- 后端接口契约（OpenAPI）：后端是否已存在？不存在则 M2 交付最小接收服务
- 视频编码 vs 无损原始：等 ML 侧需求
- 数据集导出格式：MCAP vs LeRobot（建议尽早与算法团队对齐）
- PICO 4 Ultra 企业版采购 + 相机 Enterprise 授权申请（人负责）
