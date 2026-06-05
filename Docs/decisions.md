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

## 待决（需要外部输入）

- 后端接口契约（OpenAPI）：后端是否已存在？不存在则 M2 交付最小接收服务
- 视频编码 vs 无损原始：等 ML 侧需求
- 数据集导出格式：MCAP vs LeRobot（建议尽早与算法团队对齐）
- PICO 4 Ultra 企业版采购 + 相机 Enterprise 授权申请（人负责）
