# 2026-06-06 | M0 基础设施落地（首轮）

## 做了什么

- git 基线：init + LFS + .gitattributes(UnityYAMLMerge) + push GitHub（2 commits）
- **修复致命问题**：用户全局 gitignore（~/Documents/gitignore_global.txt）含 `Assets/**/*.meta`、`*.dll` —— 本仓库已用空 excludesFile 禁用（.git/config）
- Odin Inspector 经用户确认不入库（付费许可），已 gitignore
- YIUIMCP 安装（commit 8c61809）：复制包 + **自补 cn.etetet.yiuimcp.Editor.asmdef（上游无 asmdef，原生项目不编译）** + 自补 UTO/tsconfig.json（上游缺失，tsc 跑不起来）+ npm build 成功 + manifest 加 Newtonsoft 3.2.1
- RunTests/GetTestResult 原子工具（Assets/Editor/Testing/TestRunnerTools.cs）：触发+轮询两段式，结果经 Logs/TestResults/latest.json 中转（PlayMode Domain Reload 会杀 RPC 通道）
- Tools/run-tests.ps1：直连 Unity RPC 轮询，全绿写 .gates/tests-green
- 三层防线：CLAUDE.md 章程、Docs/constitution.md 宪法、.claude/settings.json hooks（PreToolUse 危险命令+commit 门禁；SessionStart 状态注入）、skills（unity-compile/run-tests/promote-experiment）、agents（unity-tester/code-reviewer）
- 程序集结构：Main.Core(无引擎依赖)/Main/Experiment._TEMPLATE/Tests.EditMode+PlayMode/PicoTest.Editor + 冒烟测试
- M1 推进：manifest 加 PICO SDK(file:G:/Unity/PICO-Unity-Integration-SDK, commit c93a59b)/XRI 2.6.4/XR Mgmt 4.4.0/URP 14.0.9/InputSystem 1.7.0；activeInputHandler=2(Both)；ProjectSetup.cs（菜单化一键配置 Player/URP/XR/主场景，可 ExecuteMenu 触发）；Builder.cs（batchmode 构建）；Bootstrap.cs
- 重启 Unity 导入中（后台轮询 :3212/health）

## 遗留 / 下一步

1. 等 Unity 导入完成 → 验证编译（可能有 asmdef 引用名/API 错误要修）
2. ExecuteMenu 跑 PicoTest/Setup/Run All → 切 Android 平台（耗时）
3. run-tests.ps1 全绿 → 写门禁 → commit + push
4. M1.5 batchmode APK 构建验证（需关编辑器）
5. 待用户输入：后端接口契约、ML 数据格式（MCAP/LeRobot）、企业版采购

## 教训

- YIUIMCP "复制即用"在原生 Unity 项目不成立（无 asmdef / 无 tsconfig），升级上游时必须保留我们补的两个文件
- 编辑 .git/config 比从 PowerShell 传带引号的 git config 值可靠
