# UTO HTTP 接口说明

本文档以当前 `UTO/src/http-server.ts` 和 Unity 侧 `YIUIMCPServer.cs` 为准。

## 分层关系

- UnityMCP：Unity Editor 进程内的 HTTP JSON-RPC 服务
- UTO HTTP：Node.js 进程，对外暴露更容易调用的 HTTP 接口

调用链路：

```text
HTTP Client
  -> UTO HTTP (/call or /batch)
  -> MCP stdio client
  -> UnityMCP HTTP JSON-RPC (/rpc)
  -> Unity 主线程工具执行
```

## 端口规则

- UnityMCP 默认端口：`3212`
- UTO HTTP 端口：`Unity端口 + 1`
- Unity 端口优先从 `Packages/cn.etetet.yiuimcp/UTO/.port` 读取

## UnityMCP 原始接口

### `GET /health`

返回示例：

```json
{
  "status": "ok",
  "pid": 12345,
  "serverId": "guid"
}
```

用途：

- UTO 判断 Unity 是否在线
- 通过 `serverId` 判断是否发生了 Domain Reload

### `POST /rpc`

请求示例：

```json
{
  "jsonrpc": "2.0",
  "method": "Log",
  "params": {
    "message": "Hello"
  },
  "id": 1
}
```

响应示例：

```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "message": "success"
  },
  "id": "1"
}
```

说明：

- 当前 Unity 侧只实现了 `/health` 和 `/rpc`
- 没有 `/call`、`/batch`、`/tools` 这类 REST 端点，这些都属于 UTO HTTP 层

## UTO HTTP 对外接口

### `GET /health`

返回示例：

```json
{
  "status": "ok",
  "service": "uto-http-server",
  "version": "1.0.0",
  "heartbeatReady": true,
  "unityPort": 3212
}
```

### `POST /call`

请求：

```json
{
  "tool": "Log",
  "params": {
    "message": "Hello from AI"
  }
}
```

成功响应：

```json
{
  "success": true,
  "result": "success",
  "isError": false,
  "duration": 120,
  "durationSeconds": "0.12"
}
```

### `POST /batch`

请求：

```json
{
  "tools": [
    { "name": "StopPlayMode", "arguments": {} },
    { "name": "TriggerCompile", "arguments": { "Force": false } },
    { "name": "GetCompileResult", "arguments": {} }
  ]
}
```

成功响应：

```json
{
  "success": true,
  "results": [
    {
      "tool": "StopPlayMode",
      "success": true,
      "result": "StopPlayMode, SKIPPED",
      "isError": false,
      "duration": 30,
      "durationSeconds": "0.03"
    }
  ],
  "totalDuration": 1500,
  "totalDurationSeconds": "1.50"
}
```

### `GET /tools`

说明：

- 当前实现只返回最小静态列表示例
- 它不是完整工具发现接口
- 不要把它当作权威工具表使用

## 当前实际可用工具

- `Log`
- `LogError`
- `EnterPlayMode`
- `StopPlayMode`
- `TriggerCompile`
- `GetCompileResult`
- `GetConsoleLog`
- `ExecuteMenu`
- `AssertConsoleContains`

## Domain Reload 处理

UTO HTTP 层内置心跳管理器，处理方式如下：

- 通过轮询 Unity `/health` 检查可用性
- 通过 `serverId` 变化识别新实例
- 对出现 `Unknown error` 的调用，等待 Unity 恢复后再返回
- 等待超时时间由 `UTO/src/config.ts` 配置，当前默认 5 分钟

## 重要限制

- 当前 `GET /tools` 返回的不是完整工具集合
- Unity 侧 `ListTools` RPC 未实现，UTO stdio 会在失败时回退为空列表
- 批量流程的真正可靠入口是 `/batch` 和 `Config/*.ps1`
