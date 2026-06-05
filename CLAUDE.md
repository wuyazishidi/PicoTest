# PicoTest — AI 自主开发章程

PICO VR 人体数据采集应用（关节/双目视频/点云 → 机器人训练）。Unity 2022.3.16f1（`D:\Unity\UnityEditor\Unity 2022.3.16f1`）。
**先读 `Docs/constitution.md`（项目宪法，不可违反）**。完整规划见 `Docs/designs/`，技术决策见 `Docs/decisions.md`。

## 开发循环（每个需求必须走完）

1. **设计**：写 `Docs/designs/<需求>.md`（目标/方案/验收标准）→ 人审核
2. **编码**：调研性内容进 `Assets/Experiments/<实验名>/`（复制 `_TEMPLATE` 模板）；确定性内容进 `Assets/Main/`
3. **编译循环**：`/unity-compile` → 修错 → 直到通过
4. **PC 级测试**：`/run-tests` 全绿（EditMode+PlayMode，自动写 `.gates/tests-green`）
5. **真机级测试**（设备到位后）：构建 APK → adb 部署 → AutoTest/真机 PlayMode
6. **人审核**：实验晋升走 `/promote-experiment`；UI 走截图审核流
7. **整合 + 回归 + commit**（hooks 强制：无 tests-green 标记不允许 commit）
8. **记日志**：`Docs/journal/YYYY-MM-DD-<主题>.md`（做了什么/为何/测试结果/遗留）

## 命令速查（YIUIMCP，前置条件：Unity 编辑器已打开本项目）

```powershell
# 编译（完整流程：停止 PlayMode → 触发编译 → 等待 Domain Reload → 返回结果）
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
# 读控制台日志
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\get_console_log.ps1' -NoWait 1"
# 跑测试（直连 Unity RPC，全绿写门禁标记）
powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1 [-Mode EditMode|PlayMode|All]
# 任意原子工具（参数 = Base64(UTF-8 JSON)，布尔传 1/0）
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\invoke-uto-tool.ps1' -Tool <名> -ParamsBase64 <b64>"
```

原子工具：`Log/LogError/EnterPlayMode/StopPlayMode/TriggerCompile/GetCompileResult/GetConsoleLog/ExecuteMenu/AssertConsoleContains` + 本项目扩展 `RunTests/GetTestResult`（`Assets/Editor/Testing/TestRunnerTools.cs`）。

## 已知陷阱

- `get_console_error.ps1` **名实不符**：返回的是编译状态摘要，不是错误日志列表 —— 要错误明细用 `GetConsoleLog`（logType 过滤）
- YIUIMCP 需要**编辑器开着**；batchmode（构建/命令行测试）需要**编辑器关闭** —— 互斥，切换前确认
- YIUIMCP 上游包**没有 asmdef**，我们自己补了 `Editor/cn.etetet.yiuimcp.Editor.asmdef`（升级上游时保留它和 `UTO/tsconfig.json`）
- YIUIMCP 代码硬依赖 Odin Inspector（已导入本地，**已 gitignore，不得提交**）
- UTO 端口：`Packages/cn.etetet.yiuimcp/UTO/.port`（默认 3212，UTO=+1）
- **必须用 `127.0.0.1` 访问 YIUIMCP**（只绑 IPv4；`localhost` 可能解析为 ::1 → 误判服务不在线）
- PowerShell 5.1 改中文文件会 GBK 乱码 —— 文件修改一律用 Edit/Write 工具，禁止 `Get-Content|-replace|Set-Content`
- **含中文的 .ps1 必须 UTF-8 with BOM**（无 BOM 时 PS5.1 按 GBK 解析，中文尾字节吞换行 → 语法错误）；Write 工具写完 .ps1 后需补 BOM：`[IO.File]::WriteAllBytes($p, [byte[]](0xEF,0xBB,0xBF) + [IO.File]::ReadAllBytes($p))`
- 全局 gitignore 有害规则已被本仓库禁用（`.git/config` 的 excludesFile 指向空文件），**meta 文件必须提交**

## 程序集结构（asmdef 强制隔离）

- `Main.Core`（`Assets/Main/Core/`）：**零 UnityEngine 依赖**的纯 C#（采集调度/序列化/协议）—— 新逻辑优先放这里
- `Main`：MonoBehaviour 薄壳，引用 Main.Core；**禁止引用任何 Experiment.\***
- `Experiment.<名>`：可引用 Main/Main.Core，反向禁止
- `Tests.EditMode` / `Tests.PlayMode`：测试程序集
- `PicoTest.Editor`：编辑器工具（Builder、RunTests 工具）

## 关键事实

- 渲染管线 URP（M1 起）；Input System = 新版（XRI 依赖）；IL2CPP/ARM64/ASTC/Linear
- PICO SDK 3.4.0 本地于 `G:\Unity\PICO-Unity-Integration-SDK`；双目相机需 Enterprise 授权；24 关节需 Motion Tracker 配件
- 数据 Schema 版本：`PicoTest.Core.CoreInfo.SchemaVersion`，变更必须递增并记录 decisions.md
- GitHub：`git@github.com:wuyazishidi/PicoTest.git`；提交信息用 conventional commits
