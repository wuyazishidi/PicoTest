# 如何扩展 Unity 原子工具

本文档基于当前 `YIUIMCPToolsRegistry`、`YIUIMCPBaseExecutor<T>` 和现有工具实现整理。

## 1. 注册规则

一个工具要被自动发现，需要同时满足：

- 类继承 `YIUIMCPBaseExecutor<T>`
- 类带有 `[YIUIMCPTools("工具名", "描述")]`
- 参数类型 `T` 继承 `YIUIMCPBaseParams`
- 类能被无参构造创建

不需要手动修改注册表。

## 2. 推荐放置位置

当前仓库习惯放在：

- `Packages/cn.etetet.yiuimcp/Editor/UnityMCP/Tools/`

只要在 Unity 可编译的 `Editor` 程序集范围内，放在别的 `Editor` 目录也可以。

## 3. 基础模板

```csharp
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEditor;
using YIUIFramework.Editor.MCP;

[HideLabel]
[HideReferenceObjectPicker]
public class ExecuteMenuParams : YIUIMCPBaseParams
{
    [LabelText("菜单路径")]
    public string menuPath;
}

[YIUIMCPTools("ExecuteMenu", "执行Unity菜单命令")]
public class YIUIMCPTools_ExecuteMenu : YIUIMCPBaseExecutor<ExecuteMenuParams>
{
    protected override async Task<YIUIMCPResult> Run(ExecuteMenuParams data)
    {
        if (string.IsNullOrWhiteSpace(data.menuPath))
        {
            return YIUIMCPResult.FailureLog("菜单路径不能为空");
        }

        bool success = EditorApplication.ExecuteMenuItem(data.menuPath);
        await Task.CompletedTask;

        return success
            ? YIUIMCPResult.Success($"成功执行菜单命令: {data.menuPath}")
            : YIUIMCPResult.FailureLog($"菜单命令执行失败: {data.menuPath}");
    }
}
```

## 4. 参数约定

`YIUIMCPBaseParams` 已内置：

- `timeoutMs`
- `delayBeforeMs`
- `delayAfterMs`

说明：

- `timeoutMs` 默认 30000ms
- `delayBeforeMs` 默认 0ms
- `delayAfterMs` 默认 1000ms
- 这些逻辑由 `YIUIMCPExecutor.ExecuteAsync` 统一处理

## 5. 返回约定

统一返回 `YIUIMCPResult`：

```csharp
return YIUIMCPResult.Success("Done");
return YIUIMCPResult.Failure("Failed");
return YIUIMCPResult.FailureLog("Failed");
```

底层结构只有两个核心字段：

- `success`
- `message`

## 6. 当前已有工具参考

| 工具名 | 文件 | 说明 |
|------|------|------|
| `Log` | `YIUIMCPTools_Log.cs` | 普通日志 |
| `LogError` | `YIUIMCPTools_Log.cs` | 错误日志 |
| `EnterPlayMode` | `YIUIMCPTools_Compile.cs` | 进入运行模式 |
| `StopPlayMode` | `YIUIMCPTools_Compile.cs` | 退出运行模式 |
| `TriggerCompile` | `YIUIMCPTools_Compile.cs` | 触发编译 |
| `GetCompileResult` | `YIUIMCPTools_Compile.cs` | 获取编译结果 |
| `GetConsoleLog` | `YIUIMCPTools_Console.cs` | 获取控制台日志 |
| `ExecuteMenu` | `YIUIMCPTools_ExecuteMenu.cs` | 执行菜单 |
| `AssertConsoleContains` | `YIUIMCPTools_AssertConsoleContains.cs` | 断言日志包含关键词 |

## 7. 注意事项

### 所有 Unity API 都必须在主线程

不要自行在线程池里直接访问 Unity API。当前框架会通过 Dispatcher 把执行切回主线程。

### 不要用不存在的旧接口名

旧文档里出现过这些过期名字：

- `GetCompileStatus`
- `forceCompilation`
- `ListTools` 已实现

当前仓库里它们都不是可直接依赖的真实接口。

### 编译相关工具的真实顺序

推荐顺序：

1. `StopPlayMode`
2. `TriggerCompile`
3. `GetCompileResult`
