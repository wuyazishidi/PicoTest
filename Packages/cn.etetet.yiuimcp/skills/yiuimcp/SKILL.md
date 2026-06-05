---
name: yiuimcp
description: 面向嵌入 `Packages/cn.etetet.yiuimcp` 的 Unity 项目的 CLI-first 自动化 skill。适用于让 AI 通过包内的 PowerShell 流程脚本驱动 Unity，而不是逐个执行传统 MCP tool call。可用于编译 Unity、读取控制台输出、执行包内 CLI flow、通过脚本入口调用 YIUI MCP 工具，以及围绕 `Config/*.ps1` 扩展新的高聚合 CLI 工作流。
---

# Yiuimcp

## Overview

把 `Packages/cn.etetet.yiuimcp/Config/*.ps1` 视为首选自动化入口。优先使用“一条 CLI 流程命令返回最终结果”的方式，而不是把任务拆成多个低层 RPC 式调用。

这个 skill 只适用于包含 `Packages/cn.etetet.yiuimcp` 的 Unity 工程，不是通用 Unity skill。

## Workflow

### 1. 先确认当前工程支持这个 skill

检查当前工作区里是否存在这些路径：

- `Packages/cn.etetet.yiuimcp/Config`
- `Packages/cn.etetet.yiuimcp/UTO`
- `Packages/cn.etetet.yiuimcp/UTO/.port`

如果这些路径不存在，就不要继续使用这个 skill，回退到普通代码分析流程。

### 2. 优先使用最高层的 CLI 入口

优先选择语义最完整、最贴近任务目标的脚本：

- 编译 / 编译验证：
  `Packages/cn.etetet.yiuimcp/Config/compile-unity-flow.ps1`

- 读取控制台日志：
  `Packages/cn.etetet.yiuimcp/Config/get_console_log.ps1`

- 读取编译结果风格的错误摘要：
  `Packages/cn.etetet.yiuimcp/Config/get_console_error.ps1`

- 以自定义参数调用单个工具：
  `Packages/cn.etetet.yiuimcp/Config/invoke-uto-tool.ps1`

只有在下面这些情况才往下层走：

- 用户明确要求修改编排实现
- 当前需要的 flow 还不存在
- 脚本本身行为异常或明显损坏

### 3. 从项目根目录执行脚本

工作目录使用项目根目录，调用方式类似：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
```

注意：

- 这些脚本使用的是 `[bool]` 参数
- 命令行里优先传 `1` 和 `0`
- 不要把 `"$True"`、`"$False"` 这种字面量字符串直接当普通文本传进去

### 4. 先理解每个脚本真实在做什么

- `compile-unity-flow.ps1`
  实际执行 `StopPlayMode -> TriggerCompile -> GetCompileResult`

- `get_console_log.ps1`
  实际调用 `GetConsoleLog`

- `get_console_error.ps1`
  虽然名字看起来像“获取错误日志”，但当前实际上调用的是 `GetCompileResult`，因此它更像“编译结果摘要”，不是原始错误日志抓取器

- `invoke-uto-tool.ps1`
  实际调用 UTO `/call`，是自定义工具调用的通用兜底入口

### 5. 自定义参数场景优先用 `invoke-uto-tool.ps1`

传参前，先把 JSON 参数编码成 UTF-8 Base64：

```powershell
$json = '{"message":"Hello from script"}'
$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
& '.\Packages\cn.etetet.yiuimcp\Config\invoke-uto-tool.ps1' -Tool 'Log' -ParamsBase64 $base64 -NoWait 1
```

常见参数示例：

```json
{"message":"text"}
```

```json
{"Force":false}
```

```json
{"menuPath":"Assets/Refresh"}
```

```json
{"keyword":"编译完毕"}
```

### 6. 了解当前真实可用的工具面

这个包当前暴露的 Unity 侧工具包括：

- `Log`
- `LogError`
- `EnterPlayMode`
- `StopPlayMode`
- `TriggerCompile`
- `GetCompileResult`
- `GetConsoleLog`
- `ExecuteMenu`
- `AssertConsoleContains`

不要假设 `ListTools` 已经实现。也不要把 UTO `/tools` 当成权威来源，因为它目前只返回最小静态列表。

## 扩展 CLI 能力时怎么做

当用户希望增加新的 CLI 能力时，按这个顺序处理：

1. 如果已有 `Config/*.ps1` 可以表达这个流程，优先复用
2. 如果本质上只是“带参数调用单个工具”，优先文档化或包装 `invoke-uto-tool.ps1`
3. 如果这是一个高频、稳定、价值高的重复流程，就在 `Packages/cn.etetet.yiuimcp/Config/` 下新增专用 `.ps1` flow
4. 保持 CLI-first：一个命令输入，一个最终结果输出
5. 新脚本加完后，同步更新 `Packages/cn.etetet.yiuimcp/Config/README.md`

优先新增高聚合任务脚本，而不是教 AI 手工串联低层 Unity tool 调用。

## 验证方式

当你修改这些 flow，或者依赖它们来判断任务是否完成时：

1. 必要时先通过 `UTO/.port` 对应的端口检查 Unity MCP 健康状态
2. 从项目根目录执行对应的 `.ps1`
3. 读取真实控制台输出
4. 汇报真实返回步骤结果，而不是只看 exit code

如果改动涉及 C#，优先用下面这个命令验证：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
```

## 边界

- 用这个 skill 驱动现有的 YIUI CLI 编排体系
- 如果 CLI flow 已经能解决问题，就不要再把项目重新理解成传统 MCP-first
- 把 Unity `/rpc`、UTO `/call`、UTO `/batch` 视为 CLI 背后的实现层，而不是主要使用界面
