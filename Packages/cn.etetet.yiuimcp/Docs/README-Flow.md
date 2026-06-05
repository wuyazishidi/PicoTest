# PowerShell 流程脚本说明

当前仓库没有 `_flow-template.ps1` 模板文件。`Config/` 目录里实际提供的是几份已经可直接调用的脚本。

## 当前脚本清单

- `compile-unity-flow.ps1`
- `get_console_log.ps1`
- `get_console_error.ps1`
- `invoke-uto-tool.ps1`

## 共同约定

- 所有脚本都会先读取 `UTO/.port`
- UTO HTTP 端口按 `Unity端口 + 1` 计算
- 脚本会尝试清理旧的 UTO 进程
- 脚本会临时启动 `node build/index.js --http`
- 执行完成后会主动关闭本次启动的 UTO 进程

## `compile-unity-flow.ps1`

用途：

- 退出 PlayMode
- 触发编译
- 获取编译结果

参数：

```powershell
param(
    [bool]$Force = $False,
    [bool]$NoWait = $True
)
```

调用：

```powershell
.\compile-unity-flow.ps1
.\compile-unity-flow.ps1 -Force $True
.\compile-unity-flow.ps1 -NoWait $False
```

对应的 `/batch` 负载：

```json
{
  "tools": [
    { "name": "StopPlayMode", "arguments": {} },
    { "name": "TriggerCompile", "arguments": { "Force": false } },
    { "name": "GetCompileResult", "arguments": {} }
  ]
}
```

## `get_console_log.ps1`

用途：

- 调用 `GetConsoleLog`

说明：

- 当前脚本没有暴露 `logType`、`logMaxCount`、`removeStackTrace` 参数
- 如果要传复杂参数，更适合直接用 `invoke-uto-tool.ps1`

## `get_console_error.ps1`

当前实现说明：

- 脚本名称叫“获取报错日志”
- 但实际调用的是 `GetCompileResult`
- 因此它返回的是“编译结果摘要”，不是“控制台错误列表工具”的独立包装

如果需要按关键字或日志类型检查控制台，更建议直接调用：

- `GetConsoleLog`
- `AssertConsoleContains`

## `invoke-uto-tool.ps1`

用途：

- 调用单个 UTO `/call` 接口

参数：

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$Tool,
    [string]$ParamsBase64 = "",
    [bool]$NoWait = $True
)
```

示例：

```powershell
.\invoke-uto-tool.ps1 -Tool "StopPlayMode"
```

带参数示例：

```powershell
$json = '{"message":"Hello from script"}'
$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
.\invoke-uto-tool.ps1 -Tool "Log" -ParamsBase64 $base64
```

## 推荐用法

- 想做固定的批量流程：直接扩展现有 `.ps1` 脚本
- 想快速试单个工具：用 `invoke-uto-tool.ps1`
- 想做编译验证：用 `compile-unity-flow.ps1`
