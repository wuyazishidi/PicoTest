# UTO HTTP 实现总结

本文档总结当前仓库里 UTO HTTP 层已经落地的真实能力。

## 实现位置

### UTO 端

- `UTO/src/index.ts`
- `UTO/src/http-server.ts`
- `UTO/src/mcp-client.ts`
- `UTO/src/heartbeat-manager.ts`
- `UTO/src/config.ts`

### Unity 端

- `Editor/UnityMCP/Core/YIUIMCPServer.cs`
- `Editor/UnityMCP/Core/YIUIMCPDispatcher.cs`
- `Editor/UnityMCP/Core/YIUIMCPToolsRegistry.cs`
- `Editor/UnityMCP/Core/YIUIMCPExecutor.cs`
- `Editor/UnityMCP/Tools/*.cs`

## 当前架构

```text
HTTP Client
  -> UTO HTTP Server
  -> MCP Client（子进程方式启动 index.js 的 stdio 模式）
  -> UnityMCP /rpc
  -> Unity 主线程执行工具
```

## 当前已实现的关键能力

- UTO HTTP 层对外暴露 `/health`、`/call`、`/batch`、`/tools`
- 通过 `mcp-client.ts` 启动本地 MCP stdio 子进程
- 通过 `heartbeat-manager.ts` 轮询 Unity `/health`
- 利用 Unity `/health` 里的 `serverId` 判断 Domain Reload
- 对 `Unknown error` 场景进行恢复等待

## 真实端口策略

- Unity 默认端口：`3212`
- HTTP 端口偏移：`+1`
- 端口优先从 `UTO/.port` 读取

## 当前实现中的已知限制

- `/tools` 目前只返回静态示例列表，不等同于真实完整工具表
- Unity 侧没有实现 `ListTools` RPC，所以 stdio 的 `ListTools` 会回退为空
- Flow 类型虽然存在声明，但主要流程能力仍由 `/batch` 和脚本封装承担

## 为什么这层是必要的

直接调用 Unity `/rpc` 时，编译和 PlayMode 切换都可能触发 Domain Reload，导致连接中断。UTO HTTP 层额外提供了：

- 统一 REST 入口
- 心跳检测
- 恢复等待
- 批量调用
- 统一耗时输出

这让外部调用端不必自己处理域重载恢复细节。
