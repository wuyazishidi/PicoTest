# Config 脚本使用说明

本文档基于当前 `Config/` 目录下脚本源码和实际运行结果整理。

## 脚本清单

- `compile-unity-flow.ps1`
- `get_console_log.ps1`
- `get_console_error.ps1`
- `invoke-uto-tool.ps1`

## 运行前提

- Unity Editor 已打开
- Unity MCP 服务可访问
- `Packages/cn.etetet.yiuimcp/UTO` 已完成 `npm install` 和 `npm run build`
- `UTO/.port` 中记录的是当前 Unity MCP 端口

本次实测环境：

- Unity MCP 端口：`13212`
- UTO HTTP 端口：`13213`

## 通用行为

所有脚本都会：

1. 读取 `UTO/.port`
2. 计算 UTO HTTP 端口为 `Unity端口 + 1`
3. 清理旧的 UTO 进程
4. 启动 `node build/index.js --http`
5. 等待 UTO `/health` 就绪
6. 调用 UTO HTTP 接口
7. 输出结果
8. 关闭本次启动的 UTO 进程

## 推荐调用方式

从项目根目录执行时，推荐这样写：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\脚本名.ps1' 参数..."
```

说明：

- 这些脚本里使用了 `[bool]` 参数
- 在命令行里推荐传 `1` 或 `0`
- 例如 `-NoWait 1`、`-Force 0`

## 1. compile-unity-flow.ps1

### 作用

执行完整编译流程：

1. `StopPlayMode`
2. `TriggerCompile`
3. `GetCompileResult`

### 参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|------|------|
| `Force` | `bool` | `False` | 是否强制编译 |
| `NoWait` | `bool` | `True` | 是否在结束后立即退出 |

### 用法

普通编译：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
```

强制编译：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 1 -NoWait 1"
```

### 本次实测结果

- 执行成功
- 总耗时约 `3.71 秒`
- 返回：
  - `StopPlayMode, SKIPPED`
  - `TriggerCompile, TRIGGERED`
  - `GetCompileResult, Status: Compilation Complete / Result: Success, No errors!`

## 2. get_console_log.ps1

### 作用

调用 `GetConsoleLog` 工具，读取控制台日志。

### 参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|------|------|
| `Force` | `bool` | `False` | 当前脚本未使用，保留参数 |
| `NoWait` | `bool` | `True` | 是否在结束后立即退出 |

### 用法

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\get_console_log.ps1' -NoWait 1"
```

### 本次实测结果

- 执行成功
- 总耗时约 `1.17 秒`
- 返回：`GetConsoleLog, Result: Success, No logs!`

### 注意

这个脚本没有把 `GetConsoleLog` 的下列原始参数暴露出来：

- `logType`
- `logMaxCount`
- `removeStackTrace`

如果你要传这些参数，建议改用 `invoke-uto-tool.ps1`。

## 3. get_console_error.ps1

### 作用

脚本名称叫“获取报错日志”，但当前实际调用的是：

- `GetCompileResult`

所以它本质上返回的是“编译结果摘要”，不是一个独立的“控制台错误列表”工具封装。

### 参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|------|------|
| `Force` | `bool` | `False` | 当前脚本未使用，保留参数 |
| `NoWait` | `bool` | `True` | 是否在结束后立即退出 |

### 用法

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\get_console_error.ps1' -NoWait 1"
```

### 本次实测结果

- 执行成功
- 总耗时约 `1.29 秒`
- 返回：`GetCompileResult, Status: Compilation Complete / Result: Success, No errors!`

### 建议

如果你的目标是：

- 看普通日志：用 `get_console_log.ps1`
- 按关键词断言：用 `invoke-uto-tool.ps1` 调 `AssertConsoleContains`
- 看编译结果：用当前这个脚本

## 4. invoke-uto-tool.ps1

### 作用

调用单个 UTO `/call` 接口，可传任意工具名和参数，是最通用的入口。

### 参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|------|------|
| `Tool` | `string` | 必填 | 要调用的工具名 |
| `ParamsBase64` | `string` | `""` | Base64 编码后的 JSON 参数 |
| `NoWait` | `bool` | `True` | 是否在结束后立即退出 |

### 参数编码方式

`ParamsBase64` 需要传入 UTF-8 JSON 的 Base64 编码结果。

例如：

```powershell
$json = '{"message":"Hello from script"}'
$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
```

### 用法

无参数工具：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\invoke-uto-tool.ps1' -Tool 'StopPlayMode' -NoWait 1"
```

带参数工具：

```powershell
@'
$json = '{"message":"Hello from script"}'
$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
& '.\Packages\cn.etetet.yiuimcp\Config\invoke-uto-tool.ps1' -Tool 'Log' -ParamsBase64 $base64 -NoWait 1
'@ | powershell -ExecutionPolicy Bypass -
```

### 本次实测结果

- 调用工具：`Log`
- 参数：`{"message":"Config invoke-uto-tool test"}`
- 执行成功
- 耗时约 `1.16 秒`
- 返回：`Log, success`

## 可传的常见工具参数

### `Log`

```json
{"message":"文本"}
```

### `LogError`

```json
{"message":"错误文本"}
```

### `TriggerCompile`

```json
{"Force":false}
```

### `ExecuteMenu`

```json
{"menuPath":"Assets/Refresh"}
```

### `GetConsoleLog`

```json
{
  "logType": 1,
  "logMaxCount": 100,
  "removeStackTrace": true
}
```

### `AssertConsoleContains`

单关键词：

```json
{"keyword":"编译完毕"}
```

多关键词：

```json
{
  "keywordsJson":"[\"A\",\"B\"]",
  "matchAll": true,
  "ignoreCase": true,
  "useRegex": false,
  "tailCount": 200,
  "removeStackTrace": true
}
```

## 本次测试结论

- `compile-unity-flow.ps1`：正常
- `get_console_log.ps1`：正常
- `get_console_error.ps1`：正常，但语义更接近“获取编译结果”
- `invoke-uto-tool.ps1`：正常

## 当前已知注意事项

- `get_console_error.ps1` 这个名字和实际行为不完全一致
- `get_console_log.ps1` / `get_console_error.ps1` 里保留了 `Force` 参数，但当前脚本体没有使用
- 命令行调用时，布尔参数推荐传 `1/0`
