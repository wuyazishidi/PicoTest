# Unity MCP 响应丢失问题

## 问题描述

**现象**：
- Unity MCP 收到了请求（Console 有日志输出 `[YIUIMCP] ????`）
- 但调用方（UTO 或 PowerShell）没有收到响应，超时
- 连接状态显示 `CloseWait`

**时间**：2026-01-18

## 问题分析

### 1. 请求流程

```
UTO/PowerShell → HTTP POST → Unity MCP (端口 3212)
                                ↓
                        Unity MCP 收到请求
                                ↓
                        Unity MCP 处理请求（有日志）
                                ↓
                        Unity MCP 尝试发送响应
                                ↓
                        ❌ 响应丢失（调用方超时）
```

### 2. CloseWait 状态说明

**CloseWait** 表示：
- 远程端（UTO）已经关闭了连接（发送了 FIN）
- 本地端（Unity MCP）还没有关闭连接
- 这是一个**半关闭**状态

**可能原因**：
1. Unity MCP 处理请求太慢，UTO 超时后关闭了连接
2. Unity MCP 在发送响应前，连接已经被 UTO 关闭
3. Unity MCP 的响应写入失败（连接已关闭）

### 3. 为什么 Unity MCP 有日志但没有响应？

**推测**：
1. Unity MCP 收到请求 → 输出日志 `[YIUIMCP] ????`
2. Unity MCP 开始处理请求（可能在主线程排队）
3. UTO 等待 30 秒超时 → 关闭连接
4. Unity MCP 处理完成 → 尝试发送响应
5. ❌ 连接已关闭 → 响应发送失败 → 留下 CloseWait 连接

### 4. 为什么检查方法返回 0 个连接？

**用户使用的检查方法**：
```csharp
var properties = IPGlobalProperties.GetIPGlobalProperties();
var connections = properties.GetActiveTcpConnections();
var closeWaitConnections = connections
    .Where(c => c.LocalEndPoint.Port == _port && c.State == TcpState.CloseWait)
    .ToList();
```

**PowerShell 检查方法**：
```powershell
Get-NetTCPConnection -LocalPort 3212 -State CloseWait
```

**可能原因**：
1. **时机问题**：Unity MCP 启动时还没有 CloseWait 连接，连接是在后续请求中产生的
2. **权限问题**：C# 的 `IPGlobalProperties` 可能需要管理员权限才能看到所有连接
3. **平台差异**：Windows 上 `IPGlobalProperties` 可能不完整

## 解决方案

### 方案 1：增加 Unity MCP 的响应超时处理

在 `YIUIMCPServer.cs` 的 `HandleRpc` 方法中：
- 检测连接是否已关闭
- 如果连接已关闭，不尝试写入响应
- 记录警告日志

### 方案 2：增加 UTO 的超时时间

当前 UTO 的超时是 30 秒，可能不够。考虑：
- 增加到 60 秒
- 或者根据工具类型动态调整超时

### 方案 3：Unity MCP 异步处理请求

当前 Unity MCP 可能在主线程处理请求，导致延迟。考虑：
- 立即返回响应（异步处理）
- 或者使用后台线程处理

### 方案 4：修复 CloseWait 连接泄漏

**根本原因**：Unity MCP 没有正确关闭连接。

**修复方法**：
在 `HandleRpc` 方法中，确保在所有情况下都调用 `response.Close()`：
```csharp
try
{
    // ... 处理请求 ...
    response.Close();
}
catch (Exception e)
{
    YIUIMCPLog.LogError($"RPC write-back error: {e}");
    try
    {
        response.Close(); // 确保关闭
    }
    catch { }
}
```

## 测试方法

### 1. 测试 Unity MCP 是否响应

```powershell
$body = '{"jsonrpc":"2.0","id":1,"method":"Log","params":{"message":"测试"}}';
Invoke-RestMethod -Uri 'http://127.0.0.1:3212/rpc' -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 5
```

**预期**：
- Unity Console 有日志：`[YIUIMCP] 测试`
- PowerShell 收到响应：`{"jsonrpc":"2.0","result":{...},"id":"1"}`

### 2. 检查 CloseWait 连接

```powershell
Get-NetTCPConnection -LocalPort 3212 -State CloseWait
```

**预期**：
- 正常情况下应该是 0 个
- 如果有 CloseWait 连接，说明有连接泄漏

### 3. 检查 Unity MCP 日志

在 Unity Console 中搜索：
- `[YIUIMCP]` - 查看所有 MCP 日志
- `RPC write-back error` - 查看响应写入错误
- `运行状况回写错误` - 查看健康检查错误

## 临时解决方案

**重启 Unity Editor**：
- 清理所有 CloseWait 连接
- 重新初始化 Unity MCP

## 长期解决方案

1. **优化 Unity MCP 响应速度**
2. **增加连接管理和超时处理**
3. **改进错误日志和诊断工具**
4. **考虑使用更可靠的通信方式**（如 gRPC、WebSocket）

## 相关文件

- `Packages/cn.etetet.yiuimcp/Editor/UnityMCP/Core/YIUIMCPServer.cs`
- `Packages/cn.etetet.yiuimcp/UTO/src/index.ts`
- `Packages/cn.etetet.yiuimcp/UTO/src/http-server.ts`

## 参考资料

- [TCP CloseWait 状态说明](https://en.wikipedia.org/wiki/Transmission_Control_Protocol#Connection_termination)
- [HttpListener 最佳实践](https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener)
