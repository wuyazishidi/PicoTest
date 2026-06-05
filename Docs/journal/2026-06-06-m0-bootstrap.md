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

## 验证结果（全部通过）

- 编译：`compile-unity-flow.ps1` → "Result: Success, No errors!"
- 测试：EditMode 2/2 ✅ + PlayMode 1/1 ✅ → `.gates/tests-green` 已生成
- ProjectSetup/Run All（经 ExecuteMenu RPC）：Player/URP/XR(PXR loader)/主场景 四步全成功
- M0+M1 已 commit（25d55a6）并 push GitHub
- **APK 构建成功：Builds/PicoTest-dev.apk（74.9MB，IL2CPP/ARM64/Development）** —— M1 验收达成
- Android 工具链（借 2022.3.21f1 + 自行补全，过程坑见下）：
  - SDK 缺 cmdline-tools → 装 v8（8512546，Java 11 兼容；v16 需 Java 17 不可用）
  - NDK 是嵌套目录（NDK\android-ndk-r23b）→ Builder 加 source.properties 探测
  - build-tools 目录为空 → 写 licenses 哈希文件后 sdkmanager 装 32.0.0（交互式 y 管道不可靠，licenses 文件是 CI 标准做法）

## 新增教训（本轮调试发现）

- YIUIMCP 还缺 `com.unity.editorcoroutines` 依赖（已加 manifest + asmdef 引用）
- **YIUIMCP 只绑 127.0.0.1**：`localhost` 可能解析 ::1 误判离线 —— 一律用 127.0.0.1
- **含中文 .ps1 必须 UTF-8 with BOM**（PS5.1 无 BOM 按 GBK 解析，中文尾字节吞换行 → 语法错误）
- RPC 消息格式 `"<Tool>, <内容>"` 有前缀；PlayMode 测试触发后 HTTP 通道必断（预期），结果以 Logs/TestResults/latest.json 为准
- 2022.3.16f1 没装 Android SDK/NDK/JDK 模块，借 2022.3.21f1 的（Builder.EnsureAndroidToolchain，可用 PICOTEST_ANDROID_TOOLCHAIN 环境变量覆盖）

## 遗留 / 下一步

1. 等 Unity 导入完成 → 验证编译（可能有 asmdef 引用名/API 错误要修）
2. ExecuteMenu 跑 PicoTest/Setup/Run All → 切 Android 平台（耗时）
3. run-tests.ps1 全绿 → 写门禁 → commit + push
4. M1.5 batchmode APK 构建验证（需关编辑器）
5. 待用户输入：后端接口契约、ML 数据格式（MCAP/LeRobot）、企业版采购

## 教训

- YIUIMCP "复制即用"在原生 Unity 项目不成立（无 asmdef / 无 tsconfig），升级上游时必须保留我们补的两个文件
- 编辑 .git/config 比从 PowerShell 传带引号的 git config 值可靠
