# UTO 与 UnityMCP 协作手册

本文档描述当前 `cn.etetet.yiuimcp` 包的真实协作方式，而不是早期设计草案。

## 1. 架构角色

### UnityMCP

- 运行在 Unity Editor 进程内
- 对外提供本地 HTTP 服务
- 负责真正执行 Unity 原子工具
- 所有 Unity API 调用都必须切回主线程

### UTO

- 运行在 Node.js 进程内
- 可作为 MCP stdio 代理运行
- 也可作为 HTTP 编排层运行
- 负责批量调用、心跳检测、等待 Unity 从 Domain Reload 中恢复

## 2. 实际链路

### 直接 RPC

```text
Client -> UnityMCP /rpc -> Dispatcher -> Tool
```

### 走 UTO HTTP

```text
Client -> UTO /call 或 /batch -> MCP stdio 子进程 -> UnityMCP /rpc -> Tool
```

## 3. 端口规则

- UnityMCP 默认端口：`3212`
- UTO HTTP 端口：`Unity端口 + 1`
- 实际 Unity 端口优先从 `UTO/.port` 读取

## 4. Unity 端核心组件

- `YIUIMCPServer`
  负责 `/health` 与 `/rpc`

- `YIUIMCPServerHelper`
  负责自动启动、健康检查、重启和生命周期管理

- `YIUIMCPDispatcher`
  负责把后台线程请求切回 Unity 主线程

- `YIUIMCPToolsRegistry`
  通过反射扫描 `[YIUIMCPTools]` 和 `[YIUIMCPFlow]`

- `YIUIMCPExecutor`
  统一处理前置延迟、超时、后置延迟

## 5. UTO 端核心组件

- `index.ts`
  MCP stdio 入口，负责转发工具调用到 Unity `/rpc`

- `mcp-client.ts`
  以子进程方式拉起 `index.js` 的 stdio 模式

- `http-server.ts`
  提供 `/health`、`/call`、`/batch`、`/tools`

- `heartbeat-manager.ts`
  轮询 Unity `/health`，检测 `serverId` 变化，等待重连稳定

## 6. 当前工具集合

- `Log`
- `LogError`
- `EnterPlayMode`
- `StopPlayMode`
- `TriggerCompile`
- `GetCompileResult`
- `GetConsoleLog`
- `ExecuteMenu`
- `AssertConsoleContains`

## 7. 编译与断线恢复

典型编译流程：

1. `StopPlayMode`
2. `TriggerCompile`
3. `GetCompileResult`

其中：

- `TriggerCompile` 可能导致 Domain Reload
- UTO 心跳层会通过 Unity `/health` 轮询恢复状态
- Unity `/health` 返回的 `serverId` 用于区分新旧实例

## 8. 重要事实与限制

- 当前 `ListTools` 没有在 Unity 侧实现
- 当前 `GET /tools` 是静态最小列表，不是权威发现机制
- `compile-unity-flow` Flow 类型已声明，但并未承担完整编排职责
- 真正可直接使用的流程封装目前主要在 `Config/*.ps1`

## 9. 什么时候用哪一层

- 只想做最底层调试：直接调 Unity `/rpc`
- 想做自动恢复、批量调用、编译流程：用 UTO `/call` 或 `/batch`
- 想给外部脚本直接调用：优先用 `Config/*.ps1`
