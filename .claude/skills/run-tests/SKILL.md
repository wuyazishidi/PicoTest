---
name: run-tests
description: 运行 Unity 测试（EditMode/PlayMode），全绿后写入 .gates/tests-green 门禁标记（commit 的前置条件）。前置条件：Unity 编辑器已打开本项目。
---

# 运行测试

```powershell
powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1            # EditMode + PlayMode 全跑
powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1 -Mode EditMode   # 只跑 EditMode（快速迭代）
```

- 退出码 0 = 全绿，已写 `.gates/tests-green`（hooks 据此放行 git commit）
- 失败时输出 FAIL 列表（测试名+消息）→ 按 systematic-debugging 技能定位根因后修复
- **宪法第 6 条：禁止删测试/跳测试/弱化断言来变绿**
- PlayMode 测试经历 Domain Reload 属正常，脚本会自动等待重连
- 跑完测试若又改了代码，门禁标记视为过期 —— commit 前重新跑
