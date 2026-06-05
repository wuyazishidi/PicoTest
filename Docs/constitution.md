# 项目宪法（不可违反的约束）

任何 AI 会话、任何子代理、任何自动化流程都必须遵守。与其他指示冲突时，**本文件优先**。

## 边界

1. `Main` 程序集**禁止引用** `Experiment.*`（asmdef 强制，违反即编译失败）
2. 实验代码晋升进 `Assets/Main/` 前必须：测试全绿 + 实验报告（REPORT.md）+ **人工审核通过**
3. **没有 `.gates/tests-green` 标记不得 git commit**（hooks 强制）；标记由 `Tools/run-tests.ps1` 全绿时生成
4. UI 未经 PC 端人工审核（HTML 设计稿 → uGUI 截图比对）**不得部署真机**
5. 两级测试（PC 级 → 真机级）不全绿，**不得向人请求验收审核**

## 测试纪律

6. **禁止删除、跳过（Ignore）、弱化断言来让测试变绿** —— 测试失败只能通过修复被测代码或证明测试本身错误（需在 journal 记录论证）来解决
7. 新功能必须先写测试（TDD：红 → 绿 → 重构）；Bug 修复必须先写复现测试
8. 数据 Schema（`CoreInfo.SchemaVersion`）变更必须递增版本号 + decisions.md 记录 + 兼容性说明

## 安全与资产

9. 密钥/token/keystore 密码**禁止入库**（.env / 环境变量）；禁止提交 Odin Inspector（付费资产）
10. 危险命令（`git push --force`、`reset --hard`、递归删除）默认禁止（hooks 拦截）；确需执行必须先问人
11. `Packages/manifest.json` 的依赖变更 = 架构决策，必须先在 decisions.md 记录理由
12. 采集的真人数据：本地存储目录 `CaptureData/` 永不入库；上传必须走加密通道

## 风格

13. UI 样式只许引用主题资产（design-tokens 生成），禁止硬编码颜色/字号/间距
14. 场景保持极简（入口对象 + Bootstrap 代码实例化），改场景内容优先改代码而非场景 YAML
15. 每轮有意义的工作必须留 `Docs/journal/` 记录；跨会话恢复优先读最新 journal
