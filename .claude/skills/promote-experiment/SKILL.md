---
name: promote-experiment
description: 把 Assets/Experiments/ 下的实验晋升进 Assets/Main/ 主体。必须在实验验证完成、准备整合进主体时使用 —— 它强制执行宪法的晋升门禁（测试+报告+人审）。
---

# 实验晋升流程（宪法第 2 条强制）

按顺序执行，**任何一步不通过即中止**：

1. **测试门禁**：实验必须有自己的 EditMode/PlayMode 测试且全绿（`Tools\run-tests.ps1`）
2. **实验报告**：实验文件夹内必须有 `REPORT.md`（结论 / 关键数据 / 已知风险 / 晋升后影响面）
3. **人工审核**：用 AskUserQuestion 向用户呈现报告摘要，请求批准晋升 —— **未获批准不得继续**
4. **代码迁移**：
   - 代码移入 `Assets/Main/`（纯逻辑进 `Main/Core/`，MonoBehaviour 进对应模块目录）
   - 命名空间从 `PicoTest.Experiments.<名>` 改为 `PicoTest.<模块>`
   - 实验的测试一并迁入 `Assets/Tests/`
   - 实验文件夹只保留 `REPORT.md` 归档
5. **回归**：`/unity-compile` 通过 + `Tools\run-tests.ps1` 全量全绿
6. **提交**：conventional commit（`feat(main): promote <实验名> ...`），push
7. **记日志**：`Docs/journal/` 记录晋升及遗留事项
