# cn.etetet.yiuimcp

> YIUI MCP 包：在 Unity Editor 内提供本地 HTTP JSON-RPC 服务，并附带一个可选的 UTO HTTP 编排层，用来处理编译、域重载和批量调用。

## 当前实现概览

- Unity 端服务入口位于 `Editor/UnityMCP/Core/`，默认监听 `127.0.0.1:3212`
- UTO 位于 `UTO/`，启动 HTTP 模式后监听 `Unity端口 + 1`，默认是 `3213`
- Unity 端健康检查接口为 `GET /health`
- Unity 端 RPC 接口为 `POST /rpc`
- UTO 提供 `GET /health`、`POST /call`、`POST /batch`、`GET /tools`

## 当前可用的 Unity 原子工具

- `Log`
- `LogError`
- `EnterPlayMode`
- `StopPlayMode`
- `TriggerCompile`
- `GetCompileResult`
- `GetConsoleLog`
- `ExecuteMenu`
- `AssertConsoleContains`

说明：
- 工具通过 `[YIUIMCPTools(...)]` 特性自动注册
- 当前仓库中还存在一个 `compile-unity-flow` Flow 类型声明，但实际流程执行主要由 `Config/*.ps1` 和 UTO `/batch` 完成

## 依赖

本包当前显式依赖：

```json
"com.unity.nuget.newtonsoft-json": "3.2.1"
```

如果项目里缺少该依赖，会出现 `using Newtonsoft.Json;` 相关的编译错误。

## 快速开始

### 1. 打开 Unity 工程

Unity 启动后，`YIUIMCPServerHelper` 会根据配置自动拉起 Unity 侧服务。

### 2. 安装并构建 UTO（仅在需要 HTTP 编排层时）

```bash
cd Packages/cn.etetet.yiuimcp/UTO
npm install
npm run build
```

### 3. 启动 UTO HTTP

```bash
cd Packages/cn.etetet.yiuimcp/UTO
npm run start:http
```

### 4. 调用示例

单工具调用：

```powershell
$body = @{
    tool = "Log"
    params = @{
        message = "Hello from AI"
    }
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:3213/call" -Method Post -Body $body -ContentType "application/json"
```

编译流程脚本：

```powershell
powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"
```

## 文档索引

- [Docs/README.md](Docs/README.md): 文档总览
- [Docs/UTO与UnityMCP协作手册.md](Docs/UTO与UnityMCP协作手册.md): 架构与协作方式
- [Docs/README_HTTP.md](Docs/README_HTTP.md): HTTP 接口说明
- [Docs/README-Flow.md](Docs/README-Flow.md): PowerShell 流程脚本说明
- [Docs/如何扩展Unity原子工具.md](Docs/如何扩展Unity原子工具.md): 扩展工具指南
- [Docs/UnityMCP异步编程指南.md](Docs/UnityMCP异步编程指南.md): Unity 主线程异步约束
- [Docs/强制编译规则.md](Docs/强制编译规则.md): 修改 C# 后的验证要求

## Skill

包内附带了一个可分享的 Codex skill：

- [skills/yiuimcp/SKILL.md](skills/yiuimcp/SKILL.md)

用途：

- 让 AI 以 CLI-first 的方式驱动这个包
- 优先使用 `Config/*.ps1` 作为高聚合入口
- 避免把工作方式退化成传统逐个 MCP tool call

如果别人把这个 skill 安装到自己的 Codex skills 目录里，就可以直接这样引用：

```text
Use $yiuimcp to compile this Unity project through its bundled CLI flow.
```

对应的 UI 元数据位于：

- [skills/yiuimcp/agents/openai.yaml](skills/yiuimcp/agents/openai.yaml)

## 重要说明

- `UTO/.port` 记录 Unity 端口；UTO HTTP 端口按 `Unity端口 + 1` 计算
- `GET /tools` 目前返回的是最小演示列表，不是 Unity 侧所有工具的权威来源
- 如果只需要 Unity 原子能力，不一定必须启动 UTO；但编译和域重载场景更适合走 UTO
