# YIUI MCP 文档总览

本文档索引基于当前仓库的真实结构整理，已移除旧设计稿中不存在的编号文件名和过期链接。

## 建议阅读顺序

1. [UTO与UnityMCP协作手册.md](UTO与UnityMCP协作手册.md)
2. [README_HTTP.md](README_HTTP.md)
3. [README-Flow.md](README-Flow.md)
4. [如何扩展Unity原子工具.md](如何扩展Unity原子工具.md)
5. [UnityMCP异步编程指南.md](UnityMCP异步编程指南.md)

## 文档说明

- [UTO与UnityMCP协作手册.md](UTO与UnityMCP协作手册.md)
  当前双端架构、端口规则、工具集合与协作方式。

- [基础设计.md](基础设计.md)
  以实现为准的详细设计文档，覆盖 Unity 端、UTO 端与数据流。

- [README_HTTP.md](README_HTTP.md)
  UTO HTTP 层的端点、请求格式、响应格式与限制说明。

- [README-Flow.md](README-Flow.md)
  `Config/` 下 PowerShell 脚本的用途、参数与调用方式。

- [如何扩展Unity原子工具.md](如何扩展Unity原子工具.md)
  如何新增一个 Unity 原子工具并自动注册。

- [UnityMCP异步编程指南.md](UnityMCP异步编程指南.md)
  Unity 主线程、延迟、超时与常见异步陷阱。

- [强制编译规则.md](强制编译规则.md)
  修改 C# 后必须执行的编译验证约束。

- [UTO_HTTP_使用指南.md](UTO_HTTP_使用指南.md)
  用 HTTP 直接调用 UTO 的使用示例。

- [UTO_HTTP_实现总结.md](UTO_HTTP_实现总结.md)
  UTO HTTP 实现现状与关键技术点总结。

## 当前真实工具集合

- `Log`
- `LogError`
- `EnterPlayMode`
- `StopPlayMode`
- `TriggerCompile`
- `GetCompileResult`
- `GetConsoleLog`
- `ExecuteMenu`
- `AssertConsoleContains`

## 已知实现限制

- `GET /tools` 当前只返回最小静态列表，不是完整工具发现接口
- Unity 侧 `ListTools` RPC 尚未实现，UTO stdio 端会在失败时返回空列表
- Flow 类型目前只声明了 `compile-unity-flow`，真正的流程执行主要依赖 `Config/*.ps1` 和 UTO `/batch`
