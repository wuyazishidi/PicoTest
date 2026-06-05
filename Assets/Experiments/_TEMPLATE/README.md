# 实验模板

新实验 = 复制本文件夹 → 重命名（如 `Exp-BodyTracking`）→ 改 asmdef 的 `name` 为 `Experiment.<实验名>`。

## 规则（见 Docs/constitution.md）

1. 实验程序集**可以引用** `Main` / `Main.Core`；**反向禁止**（Main 引用实验 = 编译失败，asmdef 强制）
2. 实验内允许快糙猛地验证想法，但**晋升进 Main 前必须**：
   - 补齐 EditMode/PlayMode 测试并全绿
   - 产出实验报告（本文件夹 `REPORT.md`：结论/数据/风险）
   - 人工审核通过（`/promote-experiment` 流程）
3. 晋升后实验文件夹保留 REPORT.md 归档，代码移入 `Assets/Main/`
