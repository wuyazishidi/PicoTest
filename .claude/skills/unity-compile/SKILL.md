---
name: unity-compile
description: 编译 Unity 项目并取回编译结果/错误。C# 代码改动后必须用它验证编译。前置条件：Unity 编辑器已打开本项目。
---

# Unity 编译流程

从项目根目录执行（会自动管理 UTO 进程、处理 Domain Reload）：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
```

- 输出含 `Result: Success, No errors!` = 编译通过
- 失败时输出含错误列表（最多 10 条）；要完整错误日志再执行：
  ```powershell
  powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\get_console_log.ps1' -NoWait 1"
  ```
- `-Force 1` = 清缓存全量重编（仅怀疑增量编译异常时用）
- 若提示 Unity 不在线：请用户打开 Unity 编辑器，**不要**自行用 batchmode 替代（与编辑器互斥）
- 修复错误后重新执行，直到通过。**编译不过禁止继续写新代码**
