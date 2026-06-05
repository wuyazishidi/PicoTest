# UnityMCP 异步编程指南

本文档以当前 `YIUIMCPExecutor`、`YIUIMCPDispatcher` 和现有工具实现为准。

## 1. 基本原则

- Unity API 只能在主线程调用
- HTTP 请求进入点不在 Unity 主线程
- 工具最终执行必须由 Dispatcher 切回主线程
- 延迟与超时不要依赖 Unity 主线程外的错误用法

## 2. 当前框架如何处理

### 执行入口

- `YIUIMCPServer` 收到 `/rpc`
- `YIUIMCPDispatcher.Dispatch(...)` 切回主线程
- `YIUIMCPToolsRegistry.Invoke(...)` 解析参数并执行工具
- `YIUIMCPExecutor.ExecuteAsync(...)` 统一处理延迟和超时

### 延迟实现

当前 `YIUIMCPExecutor` 使用 `EditorApplication.update` 实现主线程安全延迟，而不是直接依赖 `Task.Delay()`

## 3. 推荐写法

```csharp
public class YIUIMCPTools_Log : YIUIMCPBaseExecutor<LogParams>
{
    protected override async Task<YIUIMCPResult> Run(LogParams data)
    {
        YIUIMCPLog.Log(data.message);
        await Task.CompletedTask;
        return YIUIMCPResult.Success();
    }
}
```

说明：

- 直接写工具逻辑
- 前后延迟和超时由框架统一处理

## 4. 不推荐写法

### 不要在后台线程直接访问 Unity API

```csharp
// 错误示意
Task.Run(() =>
{
    var obj = Selection.activeObject;
});
```

### 不要把框架级延迟逻辑重复写进工具里

通常不需要在每个工具里自己再做一遍额外的等待封装，除非这个工具确实有特殊节拍需求。

## 5. 参数中的异步相关字段

所有参数继承 `YIUIMCPBaseParams` 后自动拥有：

- `timeoutMs`
- `delayBeforeMs`
- `delayAfterMs`

默认值：

- `timeoutMs = 30000`
- `delayBeforeMs = 0`
- `delayAfterMs = 1000`

部分工具会主动覆盖，比如编译与 PlayMode 切换工具会把 `delayAfterMs` 改为 `0`。

## 6. 编译与 Domain Reload 的特殊性

- `TriggerCompile` 会触发编译和潜在的 Domain Reload
- 编译相关流程不能假设一次 RPC 长连接一定完整返回
- 如果是外部调用，推荐走 UTO `/batch` 或 `compile-unity-flow.ps1`

## 7. 审查清单

- 是否直接在工具里访问 Unity API
- 是否依然保持主线程执行
- 是否误用了旧接口名或旧参数名
- 是否真的需要额外 `await`
- 修改 `.cs` 后是否跑了 `compile-unity-flow.ps1`
